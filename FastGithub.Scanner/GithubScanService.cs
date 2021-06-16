﻿using FastGithub.Scanner.Middlewares;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace FastGithub.Scanner
{
    [Service(ServiceLifetime.Singleton, ServiceType = typeof(IGithubScanService))]
    sealed class GithubScanService : IGithubScanService
    {
        private readonly GithubMetaService metaService;
        private readonly ILogger<GithubScanService> logger;
        private readonly GithubContextCollection results = new();

        private readonly InvokeDelegate<GithubContext> fullScanDelegate;
        private readonly InvokeDelegate<GithubContext> resultScanDelegate;

        public GithubScanService(
            GithubMetaService metaService,
            ILogger<GithubScanService> logger,
            IPipelineBuilder<GithubContext> pipelineBuilder)
        {
            this.metaService = metaService;
            this.logger = logger;

            this.fullScanDelegate = pipelineBuilder
                .New()
                .Use<ConcurrentMiddleware>()
                .Use<ScanOkLogMiddleware>()
                .Use<StatisticsMiddleware>()
                .Use<PortScanMiddleware>()
                .Use<HttpsScanMiddleware>()
                .Build();

            this.resultScanDelegate = pipelineBuilder
                .New()
                .Use<ScanOkLogMiddleware>()
                .Use<StatisticsMiddleware>()
                .Use<PortScanMiddleware>()
                .Use<HttpsScanMiddleware>()
                .Build();
        }

        public async Task ScanAllAsync(CancellationToken cancellationToken = default)
        {
            this.logger.LogInformation("完整扫描开始");
            var meta = await this.metaService.GetMetaAsync(cancellationToken);
            if (meta != null)
            {
                var scanTasks = meta.ToGithubContexts().Select(ctx => ScanAsync(ctx));
                await Task.WhenAll(scanTasks);
            }

            this.logger.LogInformation("完整扫描结束");

            async Task ScanAsync(GithubContext context)
            {
                await this.fullScanDelegate(context);
                if (context.Available == true)
                {
                    this.results.Add(context);
                }
            }
        }

        public async Task ScanResultAsync()
        {
            this.logger.LogInformation("结果扫描开始");

            var contexts = this.results.ToArray();
            foreach (var context in contexts)
            {
                await this.resultScanDelegate(context);
            }

            this.logger.LogInformation("结果扫描结束");
        }

        public IPAddress? FindBestAddress(string domain)
        {
            return this.results.FindBestAddress(domain);
        }


        public bool IsAvailable(string domain, IPAddress address)
        {
            return this.results.TryGet(domain, address, out var context) && context.Available;
        }
    }
}
