﻿using System;
using System.Threading;
using Marten;
using Marten.Events.Daemon.Resiliency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using N8T.Core.Helpers;
using Weasel.Core;
using Weasel.Postgresql;

namespace N8T.Infrastructure.Marten
{
    public class Config
    {
        private const string DefaultSchema = "public";

        public string ConnectionString { get; set; } = default!;

        public string WriteModelSchema { get; set; } = DefaultSchema;
        public string ReadModelSchema { get; set; } = DefaultSchema;

        public bool ShouldRecreateDatabase { get; set; }

        public DaemonMode DaemonMode { get; set; } = DaemonMode.Disabled;
    }

    public static class Extensions
    {
        private const string DefaultConfigKey = "EventStore";

        public static IServiceCollection AddMarten(this IServiceCollection services, IConfiguration config,
            Action<StoreOptions>? configureOptions = null, string configKey = DefaultConfigKey)
        {
            var martenConfig = config.GetSection(configKey).Get<Config>();

            services
                .AddScoped<IIdGenerator, MartenIdGenerator>();

            var documentStore = services
                .AddMarten(options =>
                {
                    SetStoreOptions(options, martenConfig, configureOptions);
                })
                .InitializeStore();

            SetupSchema(documentStore, martenConfig, 1);

            return services;
        }

        private static void SetupSchema(IDocumentStore documentStore, Config martenConfig, int retryLeft = 1)
        {
            try
            {
                if (martenConfig.ShouldRecreateDatabase)
                    documentStore.Advanced.Clean.CompletelyRemoveAll();

                // TODO
                //using (NoSynchronizationContextScope.Enter())
                {
                    documentStore.Schema.ApplyAllConfiguredChangesToDatabaseAsync().Wait();
                }
            }
            catch
            {
                if (retryLeft == 0) throw;

                Thread.Sleep(1000);
                SetupSchema(documentStore, martenConfig, --retryLeft);
            }
        }

        private static void SetStoreOptions(StoreOptions options, Config config,
            Action<StoreOptions>? configureOptions = null)
        {
            options.Connection(config.ConnectionString);
            options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

            var schemaName = Environment.GetEnvironmentVariable("SchemaName");
            options.Events.DatabaseSchemaName = schemaName ?? config.WriteModelSchema;
            options.DatabaseSchemaName = schemaName ?? config.ReadModelSchema;

            options.UseDefaultSerialization(nonPublicMembersStorage: NonPublicMembersStorage.NonPublicSetters,
                enumStorage: EnumStorage.AsString);
            options.Projections.AsyncMode = config.DaemonMode;

            configureOptions?.Invoke(options);
        }
    }
}
