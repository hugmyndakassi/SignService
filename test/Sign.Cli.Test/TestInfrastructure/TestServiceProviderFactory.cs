// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sign.Core;

namespace Sign.Cli.Test
{
    internal sealed class TestServiceProviderFactory : IServiceProviderFactory
    {
        private readonly IServiceProvider _serviceProvider;

        internal TestServiceProviderFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IServiceProvider Create(
            LogLevel logLevel = LogLevel.Information,
            ILoggerProvider? loggerProvider = null,
            Action<IServiceCollection>? addServices = null)
        {
            return _serviceProvider;
        }

        public void AddServices(Action<IServiceCollection> addServices)
        { }
    }
}
