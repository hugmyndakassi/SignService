// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Sign.Core
{
    internal sealed class NuGetSigner : RetryingSigner, IDataFormatSigner
    {
        private readonly ICertificateProvider _certificateProvider;
        private readonly ISignatureAlgorithmProvider _signatureAlgorithmProvider;
        private readonly INuGetSignTool _nuGetSignTool;

        // Dependency injection requires a public constructor.
        public NuGetSigner(
            ISignatureAlgorithmProvider signatureAlgorithmProvider,
            ICertificateProvider certificateProvider,
            INuGetSignTool nuGetSignTool,
            ILogger<IDataFormatSigner> logger)
            : base(logger)
        {
            ArgumentNullException.ThrowIfNull(signatureAlgorithmProvider, nameof(signatureAlgorithmProvider));
            ArgumentNullException.ThrowIfNull(certificateProvider, nameof(certificateProvider));
            ArgumentNullException.ThrowIfNull(nuGetSignTool, nameof(nuGetSignTool));

            _signatureAlgorithmProvider = signatureAlgorithmProvider;
            _certificateProvider = certificateProvider;
            _nuGetSignTool = nuGetSignTool;
        }

        public bool CanSign(FileInfo file)
        {
            ArgumentNullException.ThrowIfNull(file, nameof(file));

            return string.Equals(file.Extension, ".nupkg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.Extension, ".snupkg", StringComparison.OrdinalIgnoreCase);
        }

        public async Task SignAsync(IEnumerable<FileInfo> files, SignOptions options)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            using (X509Certificate2 certificate = await _certificateProvider.GetCertificateAsync())
            using (RSA rsa = await _signatureAlgorithmProvider.GetRsaAsync())
            {
                var fileTaskPairs = files
                    .Select(file => new
                    {
                        File = file,
                        Task = SignAsync(args: null, file, rsa, certificate, options)
                    })
                    .ToList();

                await Task.WhenAll(fileTaskPairs.Select(pair => pair.Task));

                List<string> failedFiles = fileTaskPairs
                    .Where(pair => !pair.Task.Result)
                    .Select(pair => pair.File.FullName)
                    .ToList();

                if (failedFiles.Count > 0)
                {
                    string failedFilePaths = string.Join(", ", failedFiles);
                    string message = string.Format(CultureInfo.CurrentCulture, Resources.SigningFailed, failedFilePaths);

                    throw new SigningException(message);
                }
            }
        }

        protected override Task<bool> SignCoreAsync(string? args, FileInfo file, RSA rsaPrivateKey, X509Certificate2 certificate, SignOptions options)
        {
            return _nuGetSignTool.SignAsync(file, rsaPrivateKey, certificate, options);
        }
    }
}
