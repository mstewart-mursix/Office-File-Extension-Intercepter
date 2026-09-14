using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OfficeWebLauncher;

internal static class Program
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private static readonly string[] Scopes = ["offline_access", "Files.ReadWrite.AppFolder"];

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "/?")
            {
                PrintHelp();
                return args.Length == 0 ? 2 : 0;
            }

            var config = LauncherConfig.Load();
            if (args[0].Equals("--signout", StringComparison.OrdinalIgnoreCase))
            {
                CredentialStore.Delete(config.CredentialName);
                Console.WriteLine("Saved Microsoft sign-in removed from this Windows account.");
                return 0;
            }

            var file = Path.GetFullPath(args[0].Trim('"'));
            if (!File.Exists(file))
                throw new FileNotFoundException("The selected file no longer exists.", file);

            if (!SupportedFiles.Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{Path.GetExtension(file)} is not configured as a supported Office file type.");

            Console.WriteLine($"Opening {Path.GetFileName(file)} in Microsoft 365 for the web…");
            var auth = new MicrosoftAuth(config);
            var accessToken = await auth.GetAccessTokenAsync();
            var webUrl = await UploadToAppFolderAsync(file, accessToken);

            OpenBrowser(webUrl);
            Console.WriteLine("Uploaded to the Office Web Launcher folder in OneDrive and opened in your browser.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Office Web Launcher could not open the file:");
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine("Press Enter to close.");
            if (!Console.IsInputRedirected)
                Console.ReadLine();
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Office Web Launcher");
        Console.WriteLine("  OfficeWebLauncher.exe <office-file>");
        Console.WriteLine("  OfficeWebLauncher.exe --signout");
        Console.WriteLine();
        Console.WriteLine("Double-click an associated Office file to upload a copy to the app's");
        Console.WriteLine("private OneDrive app folder and open its Microsoft 365 web editor.");
    }

    private static void OpenBrowser(string url)
    {
        if (OperatingSystem.IsLinux())
        {
            Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static async Task<string> UploadToAppFolderAsync(string file, string accessToken)
    {
        var encodedName = Uri.EscapeDataString(Path.GetFileName(file));
        var sessionUrl = $"https://graph.microsoft.com/v1.0/me/drive/special/approot:/{encodedName}:/createUploadSession";
        using var sessionRequest = new HttpRequestMessage(HttpMethod.Post, sessionUrl);
        sessionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        sessionRequest.Content = JsonContent("""
            {"item":{"@microsoft.graph.conflictBehavior":"rename"}}
            """);

        using var sessionResponse = await Http.SendAsync(sessionRequest);
        var sessionBody = await sessionResponse.Content.ReadAsStringAsync();
        if (!sessionResponse.IsSuccessStatusCode)
            throw GraphError("OneDrive refused to create an upload session", sessionResponse.StatusCode, sessionBody);

        var uploadUrl = JsonDocument.Parse(sessionBody).RootElement.GetProperty("uploadUrl").GetString()
            ?? throw new InvalidOperationException("OneDrive did not return an upload URL.");

        const int chunkSize = 10 * 1024 * 1024; // 32 × Graph's required 320 KiB fragment size.
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize, true);
        if (stream.Length == 0)
            throw new InvalidOperationException("OneDrive cannot upload an empty file.");

        var buffer = new byte[chunkSize];
        long offset = 0;
        while (offset < stream.Length)
        {
            var wanted = (int)Math.Min(buffer.Length, stream.Length - offset);
            var read = 0;
            while (read < wanted)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(read, wanted - read));
                if (count == 0) throw new EndOfStreamException("The file changed while it was being uploaded.");
                read += count;
            }

            using var chunkRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
            var content = new ByteArrayContent(buffer, 0, read);
            content.Headers.ContentLength = read;
            content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + read - 1, stream.Length);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            chunkRequest.Content = content;

            using var chunkResponse = await Http.SendAsync(chunkRequest);
            var chunkBody = await chunkResponse.Content.ReadAsStringAsync();
            if (!chunkResponse.IsSuccessStatusCode)
                throw GraphError("OneDrive upload failed", chunkResponse.StatusCode, chunkBody);

            offset += read;
            if (chunkResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
            {
                var root = JsonDocument.Parse(chunkBody).RootElement;
                if (root.TryGetProperty("webUrl", out var url) && !string.IsNullOrWhiteSpace(url.GetString()))
                    return url.GetString()!;
                throw new InvalidOperationException("The upload finished, but OneDrive did not return a browser link.");
            }
        }

        throw new InvalidOperationException("The OneDrive upload ended unexpectedly.");
    }

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    private static Exception GraphError(string prefix, HttpStatusCode status, string body)
    {
        try
        {
            var error = JsonDocument.Parse(body).RootElement.GetProperty("error");
            var message = error.GetProperty("message").GetString();
            return new InvalidOperationException($"{prefix} ({(int)status}): {message}");
        }
        catch
        {
            return new InvalidOperationException($"{prefix} ({(int)status}).");
        }
    }

    private sealed class MicrosoftAuth(LauncherConfig config)
    {
        private readonly LauncherConfig _config = config;
        private static string ScopeText => string.Join(' ', Scopes);

        public async Task<string> GetAccessTokenAsync()
        {
            var refreshToken = CredentialStore.Read(_config.CredentialName);
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var refreshed = await RequestTokenAsync(new Dictionary<string, string>
                {
                    ["client_id"] = _config.ClientId,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["scope"] = ScopeText
                }, allowFailure: true);

                if (refreshed is not null)
                    return SaveAndReturn(refreshed);

                CredentialStore.Delete(_config.CredentialName);
            }

            return SaveAndReturn(await InteractiveSignInAsync());
        }

        private async Task<TokenResponse> InteractiveSignInAsync()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirectUri = $"http://localhost:{port}/";
            var state = Base64Url(RandomNumberGenerator.GetBytes(24));
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

            var authorizeUrl = $"https://login.microsoftonline.com/{Uri.EscapeDataString(_config.Tenant)}/oauth2/v2.0/authorize" +
                $"?client_id={Uri.EscapeDataString(_config.ClientId)}" +
                "&response_type=code&response_mode=query" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                $"&scope={Uri.EscapeDataString(ScopeText)}" +
                $"&state={Uri.EscapeDataString(state)}" +
                $"&code_challenge={Uri.EscapeDataString(challenge)}&code_challenge_method=S256";

            Console.WriteLine("Sign in and approve OneDrive access in the browser window.");
            OpenBrowser(authorizeUrl);

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            using var network = client.GetStream();
            using var reader = new StreamReader(network, Encoding.ASCII, false, 4096, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(timeout.Token)
                ?? throw new InvalidOperationException("The sign-in callback was empty.");
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }

            var parts = requestLine.Split(' ');
            if (parts.Length < 2) throw new InvalidOperationException("The sign-in callback was invalid.");
            var query = ParseQuery(new Uri("http://localhost" + parts[1]).Query);

            var success = query.TryGetValue("code", out var code) &&
                          query.TryGetValue("state", out var returnedState) &&
                          CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(returnedState));
            var page = success
                ? "<h1>Sign-in complete</h1><p>You can close this tab. The file is being uploaded and will open shortly.</p>"
                : "<h1>Sign-in was not completed</h1><p>Return to Office Web Launcher for details.</p>";
            var pageBytes = Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><title>Office Web Launcher</title>" + page);
            var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {pageBytes.Length}\r\nConnection: close\r\n\r\n");
            await network.WriteAsync(header, timeout.Token);
            await network.WriteAsync(pageBytes, timeout.Token);

            if (!success)
            {
                var detail = query.TryGetValue("error_description", out var description) ? description : "The login response was rejected.";
                throw new InvalidOperationException(detail);
            }

            return await RequestTokenAsync(new Dictionary<string, string>
            {
                ["client_id"] = _config.ClientId,
                ["grant_type"] = "authorization_code",
                ["code"] = code!,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = verifier,
                ["scope"] = ScopeText
            }) ?? throw new InvalidOperationException("Microsoft sign-in did not return a token.");
        }

        private async Task<TokenResponse?> RequestTokenAsync(Dictionary<string, string> fields, bool allowFailure = false)
        {
            var endpoint = $"https://login.microsoftonline.com/{Uri.EscapeDataString(_config.Tenant)}/oauth2/v2.0/token";
            using var response = await Http.PostAsync(endpoint, new FormUrlEncodedContent(fields));
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                if (allowFailure) return null;
                try
                {
                    var root = JsonDocument.Parse(body).RootElement;
                    throw new InvalidOperationException(root.TryGetProperty("error_description", out var detail)
                        ? detail.GetString() ?? "Microsoft sign-in failed."
                        : "Microsoft sign-in failed.");
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException($"Microsoft sign-in failed ({(int)response.StatusCode}).");
                }
            }

            return JsonSerializer.Deserialize<TokenResponse>(body)
                ?? throw new InvalidOperationException("Microsoft returned an unreadable sign-in response.");
        }

        private string SaveAndReturn(TokenResponse token)
        {
            if (string.IsNullOrWhiteSpace(token.AccessToken))
                throw new InvalidOperationException("Microsoft did not return an access token.");
            if (!string.IsNullOrWhiteSpace(token.RefreshToken))
                CredentialStore.Write(_config.CredentialName, token.RefreshToken);
            return token.AccessToken;
        }

        private static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
                          pair => Uri.UnescapeDataString((pair.Length > 1 ? pair[1] : "").Replace('+', ' ')));

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class LauncherConfig
    {
        public string ClientId { get; init; } = "";
        public string Tenant { get; init; } = "common";
        [JsonIgnore] public string CredentialName => $"OfficeWebLauncher:{ClientId}:{Tenant}";

        public static LauncherConfig Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "OfficeWebLauncher.json");
            if (!File.Exists(path))
                throw new InvalidOperationException($"Configuration is missing. Run Setup.ps1 in {AppContext.BaseDirectory}");

            var config = JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("OfficeWebLauncher.json is invalid.");

            if (!Guid.TryParse(config.ClientId, out _))
                throw new InvalidOperationException("OfficeWebLauncher.json needs a valid Microsoft Entra Application (client) ID.");
            if (string.IsNullOrWhiteSpace(config.Tenant))
                throw new InvalidOperationException("OfficeWebLauncher.json needs a Tenant value (normally common)." );
            return config;
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; init; } = "";
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; init; }
    }

    private static class SupportedFiles
    {
        public static readonly string[] Extensions =
        [
            ".doc", ".docx", ".docm", ".dot", ".dotx", ".dotm", ".odt", ".rtf",
            ".xls", ".xlsx", ".xlsm", ".xlsb", ".xlt", ".xltx", ".xltm", ".csv", ".ods",
            ".ppt", ".pptx", ".pptm", ".pps", ".ppsx", ".ppsm", ".pot", ".potx", ".potm", ".odp",
            ".vsd", ".vsdx"
        ];
    }

    private static class CredentialStore
    {
        public static void Write(string target, string secret)
        {
            if (OperatingSystem.IsWindows())
                WindowsCredentialStore.Write(target, secret);
            else
                SecureFileCredentialStore.Write(target, secret);
        }

        public static string? Read(string target) => OperatingSystem.IsWindows()
            ? WindowsCredentialStore.Read(target)
            : SecureFileCredentialStore.Read(target);

        public static void Delete(string target)
        {
            if (OperatingSystem.IsWindows())
                WindowsCredentialStore.Delete(target);
            else
                SecureFileCredentialStore.Delete(target);
        }
    }

    private static class SecureFileCredentialStore
    {
        private static string GetDirectory()
        {
            var root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(root, "office-web-launcher", "credentials");
        }

        private static string GetPath(string target)
        {
            var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target))).ToLowerInvariant();
            return Path.Combine(GetDirectory(), name + ".token");
        }

        public static void Write(string target, string secret)
        {
            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("This credential store is supported on Linux only.");

            var directory = GetDirectory();
            Directory.CreateDirectory(directory);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var path = GetPath(target);
            var bytes = Encoding.UTF8.GetBytes(secret);
            try
            {
                using var stream = new FileStream(path, new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                });
                stream.Write(bytes);
                stream.Flush(true);
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }

        public static string? Read(string target)
        {
            var path = GetPath(target);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        public static void Delete(string target)
        {
            var path = GetPath(target);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static class WindowsCredentialStore
    {
        private const uint CredTypeGeneric = 1;
        private const uint CredPersistLocalMachine = 2;

        public static void Write(string target, string secret)
        {
            var bytes = Encoding.Unicode.GetBytes(secret);
            var blob = Marshal.AllocCoTaskMem(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new NativeCredential
                {
                    Type = CredTypeGeneric,
                    TargetName = target,
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = CredPersistLocalMachine,
                    UserName = Environment.UserName
                };
                if (!CredWrite(ref credential, 0))
                    throw new InvalidOperationException($"Windows could not securely save the sign-in (error {Marshal.GetLastWin32Error()}).");
            }
            finally
            {
                Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
                Marshal.FreeCoTaskMem(blob);
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        public static string? Read(string target)
        {
            if (!CredRead(target, CredTypeGeneric, 0, out var pointer)) return null;
            try
            {
                var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                return credential.CredentialBlobSize == 0 ? null :
                    Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            }
            finally { CredFree(pointer); }
        }

        public static void Delete(string target) => CredDelete(target, CredTypeGeneric, 0);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string? Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string UserName;
        }

        [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);
        [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);
        [DllImport("Advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr credential);
    }
}
