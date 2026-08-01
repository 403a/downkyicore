using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DownKyi.Core.Aria2cNet.Client;
using DownKyi.Core.Aria2cNet.Client.Entity;
using DownKyi.Core.Aria2cNet.Server;

namespace DownKyi.Tests;

internal sealed class Aria2TlsTestRuntime : IAsyncDisposable
{
    public const string SecureRedirectFeature = "downkyi-secure-redirect-v2";
    private readonly Process _process;
    private readonly Task<string> _standardError;
    private readonly Task<string> _standardOutput;
    private readonly TrustedRootScope _trustedRoot;
    private readonly string _workingDirectory;
    private bool _disposed;

    private Aria2TlsTestRuntime(
        Process process,
        Task<string> standardOutput,
        Task<string> standardError,
        AriaClient client,
        TrustedRootScope trustedRoot,
        string workingDirectory,
        string ariaVersion,
        string binarySha256)
    {
        _process = process;
        _standardOutput = standardOutput;
        _standardError = standardError;
        Client = client;
        _trustedRoot = trustedRoot;
        _workingDirectory = workingDirectory;
        AriaVersion = ariaVersion;
        BinarySha256 = binarySha256;
    }

    public AriaClient Client { get; }

    public string AriaVersion { get; }

    public string BinarySha256 { get; }

    public string CertificateAuthoritySource => _trustedRoot.Source;

    public static async Task<Aria2TlsTestRuntime> StartAsync(
        string binaryPath,
        X509Certificate2 trustedRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);
        ArgumentNullException.ThrowIfNull(trustedRoot);
        if (!File.Exists(binaryPath))
        {
            throw new FileNotFoundException("The aria2 integration binary was not found.", binaryPath);
        }

        AriaBinaryIntegrityVerifier.Verify(binaryPath);
        string binarySha256;
        var binary = File.OpenRead(binaryPath);
        await using (binary.ConfigureAwait(false))
        {
            binarySha256 = Convert.ToHexString(
                await SHA256.HashDataAsync(binary, cancellationToken).ConfigureAwait(false));
        }

        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"downkyi-aria2-tls-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        var rootPath = Path.Combine(workingDirectory, "trusted-root.pem");
        await File.WriteAllTextAsync(
            rootPath,
            trustedRoot.ExportCertificatePem(),
            cancellationToken).ConfigureAwait(false);
        var trustedRootScope = await TrustedRootScope.InstallAsync(
            trustedRoot,
            rootPath,
            cancellationToken).ConfigureAwait(false);
        Process? process = null;
        try
        {
            var port = GetAvailablePort();
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var secretFile = Path.Combine(workingDirectory, $".rpc-{Guid.NewGuid():N}.conf");
            await File.WriteAllTextAsync(
                secretFile,
                $"rpc-secret={token}{Environment.NewLine}",
                cancellationToken).ConfigureAwait(false);
            RestrictSecretFile(secretFile);

            var startInfo = CreateStartInfo(
                binaryPath,
                workingDirectory,
                secretFile,
                rootPath,
                port);
            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException("The aria2 TLS test process did not start.");
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            var client = new AriaClient("http://127.0.0.1", port, token);
            var version = await WaitForReadyAsync(
                process,
                client,
                cancellationToken).ConfigureAwait(false);
            DeleteSecretFile(secretFile);
            return new Aria2TlsTestRuntime(
                process,
                standardOutput,
                standardError,
                client,
                trustedRootScope,
                workingDirectory,
                version,
                binarySha256);
        }
        catch
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            process?.Dispose();
            await trustedRootScope.DisposeAsync().ConfigureAwait(false);
            DeleteDirectory(workingDirectory);
            throw;
        }
    }

    public async Task<string> AddDownloadAsync(
        Uri url,
        string outputName,
        int split,
        int maximumTries,
        IReadOnlyList<string>? headers,
        CancellationToken cancellationToken)
    {
        return await AddDownloadAsync(
            url,
            outputName,
            split,
            maximumTries,
            headers,
            httpsProxy: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> AddDownloadAsync(
        Uri url,
        string outputName,
        int split,
        int maximumTries,
        IReadOnlyList<string>? headers,
        string? httpsProxy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);
        var result = await Client.AddUriAsync(
            [url.AbsoluteUri],
            new AriaSendOption
            {
                Dir = _workingDirectory,
                Out = outputName,
                Continue = "true",
                AllowOverwrite = "true",
                AutoFileRenaming = "false",
                Split = split.ToString(CultureInfo.InvariantCulture),
                MaxConnectionPerServer = split.ToString(CultureInfo.InvariantCulture),
                MinSplitSize = "1M",
                MaxTries = maximumTries.ToString(CultureInfo.InvariantCulture),
                RetryWait = "0",
                AlwaysResume = "false",
                MaxResumeFailureTries = "0",
                Headers = headers ?? [],
                HttpsProxy = httpsProxy ?? string.Empty
            }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Result
            ?? throw new InvalidOperationException("aria2 did not return a download identifier.");
    }

    public string GetOutputPath(string outputName)
    {
        return Path.Combine(_workingDirectory, outputName);
    }

    public async Task<AriaTellStatusResult> WaitForTerminalStatusAsync(
        string gid,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await Client.TellStatus(gid).ConfigureAwait(false);
            if (status.Result is { } result
                && (string.Equals(result.Status, "complete", StringComparison.Ordinal)
                    || string.Equals(result.Status, "error", StringComparison.Ordinal)
                    || string.Equals(result.Status, "removed", StringComparison.Ordinal)))
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException("aria2 did not reach a terminal status before the test deadline.");
    }

    private static ProcessStartInfo CreateStartInfo(
        string binaryPath,
        string workingDirectory,
        string secretFile,
        string rootPath,
        int port)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        var arguments = new List<string>
        {
            $"--conf-path={secretFile}",
            "--enable-rpc=true",
            "--rpc-listen-all=false",
            "--rpc-allow-origin-all=false",
            $"--rpc-listen-port={port}",
            "--disable-ipv6=true",
            "--check-certificate=true",
            "--file-allocation=none",
            "--allow-overwrite=true",
            "--auto-file-renaming=false",
            "--continue=true",
            "--max-concurrent-downloads=4",
            "--max-connection-per-server=4",
            "--split=4",
            "--min-split-size=1M",
            "--max-tries=1",
            "--retry-wait=0",
            "--console-log-level=warn",
            "--summary-interval=0"
        };
        if (OperatingSystem.IsLinux())
        {
            arguments.Add($"--ca-certificate={rootPath}");
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task<string> WaitForReadyAsync(
        Process process,
        AriaClient client,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The aria2 TLS test process exited before RPC became ready (code {process.ExitCode}).");
            }

            try
            {
                var response = await client.GetAriaVersionAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(response.Result?.Version)
                    && response.Result.EnabledFeatures.Contains(
                        SecureRedirectFeature,
                        StringComparer.Ordinal))
                {
                    return response.Result.Version;
                }
            }
            catch (HttpRequestException error)
            {
                lastError = error;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            "aria2 RPC did not become ready before the test deadline.",
            lastError);
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void RestrictSecretFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void DeleteSecretFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var length = new FileInfo(path).Length;
        using (var stream = new FileStream(
                   path,
                   FileMode.Open,
                   FileAccess.Write,
                   FileShare.None))
        {
            stream.SetLength(length);
            stream.Write(new byte[length]);
            stream.Flush(flushToDisk: true);
        }

        File.Delete(path);
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    await Client.ForceShutdownAsync().ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                }

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }

            await Task.WhenAll(_standardOutput, _standardError).ConfigureAwait(false);
        }
        finally
        {
            _process.Dispose();
            await _trustedRoot.DisposeAsync().ConfigureAwait(false);
            DeleteDirectory(_workingDirectory);
        }
    }
}

internal sealed class TrustedRootScope : IAsyncDisposable
{
    private readonly string? _cleanupCommand;
    private readonly IReadOnlyList<string>? _cleanupArguments;

    private TrustedRootScope(
        string source,
        string? cleanupCommand = null,
        IReadOnlyList<string>? cleanupArguments = null)
    {
        Source = source;
        _cleanupCommand = cleanupCommand;
        _cleanupArguments = cleanupArguments;
    }

    public string Source { get; }

    public static async Task<TrustedRootScope> InstallAsync(
        X509Certificate2 root,
        string rootPath,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsLinux())
        {
            return new TrustedRootScope("aria2-ca-file");
        }

        if (OperatingSystem.IsMacOS())
        {
            var commonName = root.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var keychain = Path.Combine(home, "Library", "Keychains", "login.keychain-db");
            await RunProcessAsync(
                "security",
                ["add-trusted-cert", "-r", "trustRoot", "-k", keychain, rootPath],
                cancellationToken).ConfigureAwait(false);
            return new TrustedRootScope(
                "macos-user-keychain",
                "security",
                ["delete-certificate", "-c", commonName, keychain]);
        }

        await RunProcessAsync(
            "certutil",
            ["-user", "-addstore", "-f", "Root", rootPath],
            cancellationToken).ConfigureAwait(false);
        return new TrustedRootScope(
            "windows-current-user-root-store",
            "certutil",
            ["-user", "-delstore", "Root", root.SerialNumber]);
    }

    private static async Task RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The certificate trust tool did not start.");
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The certificate trust command failed with code {process.ExitCode}.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cleanupCommand != null && _cleanupArguments != null)
        {
            await RunProcessAsync(
                _cleanupCommand,
                _cleanupArguments,
                CancellationToken.None).ConfigureAwait(false);
        }
    }
}
