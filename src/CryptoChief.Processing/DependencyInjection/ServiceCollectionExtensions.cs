using CryptoChief.Processing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    public static class CryptoChiefServiceCollectionExtensions
    {
        /// <summary>Register the SDK with options configured in code.</summary>
        public static IHttpClientBuilder AddCryptoChief(
            this IServiceCollection services,
            Action<CryptoChiefClientOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.AddOptions<CryptoChiefClientOptions>()
                .Configure(configure)
                .PostConfigure(o => o.Validate());

            return services.AddCryptoChiefCore();
        }

        /// <summary>Register the SDK binding options from an <see cref="IConfiguration"/> section.</summary>
        public static IHttpClientBuilder AddCryptoChief(
            this IServiceCollection services,
            IConfiguration section)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(section);

            services.AddOptions<CryptoChiefClientOptions>()
                .Bind(section)
                .PostConfigure(o => o.Validate());

            return services.AddCryptoChiefCore();
        }

        private static IHttpClientBuilder AddCryptoChiefCore(this IServiceCollection services)
        {
            services.TryAddTransient(sp =>
            {
                var options = sp.GetRequiredService<IOptions<CryptoChiefClientOptions>>();
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                var http = factory.CreateClient(nameof(CryptoChiefClient));
                var logger = sp.GetService<ILogger<CryptoChiefClient>>();
                return new CryptoChiefClient(options, http, logger);
            });

            return services.AddHttpClient(nameof(CryptoChiefClient), (sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<CryptoChiefClientOptions>>().Value;
                client.Timeout = opts.Timeout;
            });
        }
    }
}
