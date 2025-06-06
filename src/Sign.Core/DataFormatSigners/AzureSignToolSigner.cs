// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AzureSign.Core;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class AzureSignToolSigner : IAzureSignToolDataFormatSigner
    {
        private readonly ICertificateProvider _certificateProvider;
        private readonly ISignatureAlgorithmProvider _signatureAlgorithmProvider;
        private readonly ILogger<IDataFormatSigner> _logger;
        private readonly IReadOnlyList<ISignableFileType> _signableFileTypes;
        private readonly IToolConfigurationProvider _toolConfigurationProvider;

        // Dependency injection requires a public constructor.
        public AzureSignToolSigner(
            IToolConfigurationProvider toolConfigurationProvider,
            ISignatureAlgorithmProvider signatureAlgorithmProvider,
            ICertificateProvider certificateProvider,
            ILogger<IDataFormatSigner> logger)
        {
            ArgumentNullException.ThrowIfNull(toolConfigurationProvider, nameof(toolConfigurationProvider));
            ArgumentNullException.ThrowIfNull(signatureAlgorithmProvider, nameof(signatureAlgorithmProvider));
            ArgumentNullException.ThrowIfNull(certificateProvider, nameof(certificateProvider));
            ArgumentNullException.ThrowIfNull(logger, nameof(logger));

            _signatureAlgorithmProvider = signatureAlgorithmProvider;
            _certificateProvider = certificateProvider;
            _signatureAlgorithmProvider = signatureAlgorithmProvider;
            _logger = logger;
            _toolConfigurationProvider = toolConfigurationProvider;

            _signableFileTypes = new List<ISignableFileType>()
            {
                // For PowerShell file extensions, see https://github.com/PowerShell/PowerShell/blob/2f4f585e7fe075f5c1669397ae738c554fa18391/src/System.Management.Automation/security/SecurityManager.cs#L97C1-L106C10
                new SignableFileTypeByExtension(
                    ".appx",
                    ".appxbundle",
                    ".cab",
                    ".cat",
                    ".cdxml",       // PowerShell cmdlet definition XML
                    ".dll",
                    ".eappx",
                    ".eappxbundle",
                    ".emsix",
                    ".emsixbundle",
                    ".exe",
                    ".msi",
                    ".msix",
                    ".msixbundle",
                    ".msm",
                    ".msp",
                    ".mst",
                    ".ocx",
                    ".ps1",         // PowerShell script files
                    ".ps1xml",      // PowerShell display configuration files
                    ".psd1",        // PowerShell data files
                    ".psm1",        // PowerShell module files
                    ".stl",
                    ".sys",
                    ".vbs",
                    ".vxd",
                    ".winmd"
                ),
                new DynamicsBusinessCentralAppFileType()
            };
        }

        public bool CanSign(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            foreach (ISignableFileType signableFileType in _signableFileTypes)
            {
                if (signableFileType.IsMatch(file))
                {
                    return true;
                }
            }

            return false;
        }

        public async Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            _logger.LogInformation(Resources.AzureSignToolSignatureProviderSigning, files.Count());

            TimeStampConfiguration timestampConfiguration;

            if (options.TimestampService is null)
            {
                timestampConfiguration = TimeStampConfiguration.None;
            }
            else
            {
                timestampConfiguration = new(options.TimestampService.AbsoluteUri, options.TimestampHashAlgorithm, TimeStampType.RFC3161);
            }

            using (X509Certificate2 certificate = await _certificateProvider.GetCertificateAsync())
            using (RSA rsa = await _signatureAlgorithmProvider.GetRsaAsync())
            using (AuthenticodeKeyVaultSigner signer = new(
                rsa,
                certificate,
                options.FileHashAlgorithm,
                timestampConfiguration))
            {
                // loop through all of the files here, looking for appx/eappx
                // mark each as being signed and strip appx
                await Parallel.ForEachAsync(files, async (file, state) =>
                {
                    if (!await SignAsync(signer, file, options))
                    {
                        string message = string.Format(CultureInfo.CurrentCulture, Resources.SigningFailed, file.FullName);

                        throw new SigningException(message);
                    }
                });
            }
        }

        // Inspired from https://github.com/squaredup/bettersigntool/blob/master/bettersigntool/bettersigntool/SignCommand.cs
        private async Task<bool> SignAsync(
            AuthenticodeKeyVaultSigner signer,
            FileInfo file,
            SignOptions options)
        {
            TimeSpan retry = TimeSpan.FromSeconds(5);
            const int maxAttempts = 3;
            var attempt = 1;

            do
            {
                if (attempt > 1)
                {
                    _logger.LogInformation(Resources.SigningAttempt, attempt, maxAttempts, retry.TotalSeconds);
                    await Task.Delay(retry);
                    retry = TimeSpan.FromSeconds(Math.Pow(retry.TotalSeconds, 1.5));
                }

                if (RunSignTool(signer, file, options))
                {
                    return true;
                }

                ++attempt;

            } while (attempt <= maxAttempts);

            _logger.LogError(Resources.SigningFailedAfterAllAttempts);

            return false;
        }

        private bool RunSignTool(AuthenticodeKeyVaultSigner signer, FileInfo file, SignOptions options)
        {
            FileInfo manifestFile = _toolConfigurationProvider.SignToolManifest;

            _logger.LogInformation(Resources.SigningFile, file.FullName);

            var success = false;
            var code = 0;
            const int S_OK = 0;

            try
            {
                using (var ctx = new Kernel32.ActivationContext(manifestFile))
                {
                    code = signer.SignFile(
                        file.FullName,
                        options.Description ?? string.Empty,
                        options.DescriptionUrl?.AbsoluteUri ?? string.Empty,
                        pageHashing: null,
                        _logger);
                    success = code == S_OK;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
            }

            if (success)
            {
                _logger.LogInformation(Resources.SigningSucceeded, file.FullName);
                return true;
            }

            _logger.LogError(Resources.SigningFailedWithError, code);

            return false;
        }
    }
}
