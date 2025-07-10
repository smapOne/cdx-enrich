﻿using CdxEnrich.Actions;
using CdxEnrich.ClearlyDefined;
using CdxEnrich.Config;
using CdxEnrich.FunctionalHelpers;
using CdxEnrich.Serialization;
using Microsoft.Extensions.DependencyInjection;
using PackageUrl;

namespace CdxEnrich.Tests.Actions.ReplaceLicenseByClearlyDefined
{
    internal class ReplaceLicenseByClearlyDefinedTest
    {
        private readonly Fixture _fixture = new();

        private class Fixture
        {
            public IReplaceAction CreateReplaceAction(bool useFake = true)
            {
                var serviceCollection = new ServiceCollection();
                serviceCollection.AddLogging();
                serviceCollection.AddReplaceLicenseByClearlyDefined();
                if (useFake)
                {
                    SetupClearlyDefinedClientFake(serviceCollection);
                }

                var sp = serviceCollection.BuildServiceProvider();
                return sp.GetRequiredService<IReplaceAction>();
            }

            private static void SetupClearlyDefinedClientFake(ServiceCollection serviceCollection)
            {
                var fake = new ClearlyDefinedClientFake();
                fake.SetupRequest((new PackageURL("pkg:npm/lodash@4.17.21"), PackageType.Npm.DefaultProvider),
                    SetupWithDeclaredResponse("CC0-1.0 AND MIT")
                );
                fake.SetupRequest(
                    (new PackageURL("pkg:maven/org.apache.commons/commons-lang3@3.12.0"),
                        PackageType.Maven.DefaultProvider),
                    SetupWithDeclaredResponse("Apache-2.0")
                );
                fake.SetupRequest((new PackageURL("pkg:pypi/requests@2.28.1"), PackageType.Pypi.DefaultProvider),
                    SetupWithDeclaredResponse("Apache-2.0")
                );
                fake.SetupRequest((new PackageURL("pkg:gem/rails@7.0.4"), PackageType.Gem.DefaultProvider),
                    SetupWithDeclaredResponse("MIT")
                );
                fake.SetupRequest((new PackageURL("pkg:pod/Alamofire@5.6.2"), PackageType.Pod.DefaultProvider),
                    SetupWithDeclaredResponse("MIT")
                );
                fake.SetupRequest((new PackageURL("pkg:crate/serde@1.0.152"), PackageType.Crate.DefaultProvider),
                    SetupWithDeclaredResponse("MIT OR Apache-2.0")
                );
                serviceCollection.AddTransient<IClearlyDefinedClient>(_ => fake);
            }

            private static ClearlyDefinedResponse.LicensedData SetupWithDeclaredResponse(string expression)
            {
                return new ClearlyDefinedResponse.LicensedData
                {
                    Declared = expression,
                    Facets = new ClearlyDefinedResponse.Facets
                    {
                        Core = new ClearlyDefinedResponse.Core
                        {
                            Discovered = new ClearlyDefinedResponse.Discovered()
                        }
                    }
                };
            }

            private class ClearlyDefinedClientFake : IClearlyDefinedClient
            {
                public ClearlyDefinedClientFake()
                {
                    var comparer = new ClearlyDefinedRequestComparer();
                    this._requestRegistry = new Dictionary<(PackageURL packageUrl, Provider provider), ClearlyDefinedResponse.LicensedData>(comparer);
                }

                private readonly IDictionary<(PackageURL packageUrl, Provider provider), ClearlyDefinedResponse.LicensedData> _requestRegistry;

                public void SetupRequest((PackageURL packageUrl, Provider provider) request,
                    ClearlyDefinedResponse.LicensedData licensedData)
                {
                    this._requestRegistry.Add(request, licensedData);
                }

                public Task<ClearlyDefinedResponse.LicensedData?> GetClearlyDefinedLicensedDataAsync(
                    PackageURL packageUrl,
                    Provider provider)
                {
                    if (this._requestRegistry.TryGetValue((packageUrl, provider), out var licensedData))
                    {
                        return Task.FromResult<ClearlyDefinedResponse.LicensedData?>(licensedData);
                    }

                    return Task.FromResult<ClearlyDefinedResponse.LicensedData?>(null);
                }

                private class ClearlyDefinedRequestComparer : IEqualityComparer<(PackageURL packageUrl, Provider provider)>
                {
                    public bool Equals((PackageURL packageUrl, Provider provider) x,
                        (PackageURL packageUrl, Provider provider) y)
                    {
                        return x.packageUrl.ToString() == y.packageUrl.ToString() && x.provider.Name == y.provider.Name;
                    }

                    public int GetHashCode((PackageURL packageUrl, Provider provider) x)
                    {
                        return HashCode.Combine(x.packageUrl.ToString(), x.provider.Name);
                    }
                }
            }
        }

        private static string[] GetConfigs(string startingWith)
        {
            string testFilesPath = Path.Combine(Environment.CurrentDirectory, "../../..", "Actions/ReplaceLicenseByClearlyDefined/testcases/configs");
            var files = Directory.GetFiles(testFilesPath).Where(s => Path.GetFileNameWithoutExtension(s).StartsWith(startingWith)).ToArray();
            if (files.Length == 0)
            {
                throw new Exception("No Testfiles found!");
            }

            return files;
        }

        private static CycloneDXFormat GetCycloneDxFormat(string bomPath)
        {
            var extension = Path.GetExtension(bomPath);
            CycloneDXFormat inputFormat = extension.Equals(".json", StringComparison.CurrentCultureIgnoreCase) ? CycloneDXFormat.JSON : CycloneDXFormat.XML;
            return inputFormat;
        }

        private static IEnumerable<object[]> GetInputPairs(string startingWith)
        {
            bool returnedAtLeastOneSet = false;

            foreach (string filePath in GetConfigs(startingWith))
            {
                string testFilesPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "../../..", "Actions/ReplaceLicenseByClearlyDefined/testcases/boms"));

                foreach (string bomPath in Directory.GetFiles(testFilesPath))
                {
                    yield return new object[] { Path.GetFullPath(filePath), bomPath };
                    returnedAtLeastOneSet = true;
                }
            }

            if (!returnedAtLeastOneSet)
            {
                throw new Exception("No Testfiles found!");
            }
        }

        [Test]
        [TestCaseSource(nameof(GetConfigs), new object[] { "" })]
        public void CanParseConfig(string configPath)
        {
            var configContent = File.ReadAllText(configPath);
            var parseConfigResult = ConfigLoader.ParseConfig(configContent);

            Assert.That(parseConfigResult is not Failure);
        }

        [Test]
        [TestCaseSource(nameof(GetInputPairs), new object[] { "invalid" })]
        public void InvalidBomAndConfigCombinationsReturnError(string configPath, string bomPath)
        {
            var inputFormat = GetCycloneDxFormat(bomPath);
            string bomContent = File.ReadAllText(bomPath);
            var replaceAction = this._fixture.CreateReplaceAction();

            var checkConfigResult =
                Runner.CombineBomAndConfig(BomSerialization.DeserializeBom(bomContent, inputFormat),
                        ConfigLoader.ParseConfig(File.ReadAllText(configPath))
                            .Bind(replaceAction.CheckConfig))
                    .Bind(replaceAction.CheckBomAndConfigCombination);

            Assert.That(checkConfigResult is Failure);
        }

        [Test]
        [TestCaseSource(nameof(GetConfigs), new object[] { "valid" })]
        public void ValidConfigsReturnSuccess(string configPath)
        {
            var configContent = File.ReadAllText(configPath);
            var replaceAction = this._fixture.CreateReplaceAction();
            var checkConfigResult = ConfigLoader.ParseConfig(configContent)
                .Bind(replaceAction.CheckConfig);

            Assert.That(checkConfigResult is Success);
        }

        [Test]
        [TestCaseSource(nameof(GetInputPairs), new object[] { "valid" })]
        public Task ExecuteActionCreateCorrectResults(string configPath, string bomPath)
        {
            var inputFormat = GetCycloneDxFormat(bomPath);
            string bomContent = File.ReadAllText(bomPath);
            var replaceAction = this._fixture.CreateReplaceAction();

            var executionResult =
                Runner.CombineBomAndConfig(BomSerialization.DeserializeBom(bomContent, inputFormat),
                    ConfigLoader.ParseConfig(File.ReadAllText(configPath))
                    .Bind(replaceAction.CheckConfig))
                    .Bind(replaceAction.CheckBomAndConfigCombination)
                .Map(replaceAction.Execute);

            Assert.That(executionResult is Success);

            var settings = new VerifySettings();
            settings.UseDirectory("testcases/snapshots");
            settings.UseFileName($"Config_{Path.GetFileName(configPath)}_Bom_{Path.GetFileName(bomPath)}");

            settings.AddExtraSettings(_ => _.ContractResolver = new VerifyContractResolver());
            return Verify(executionResult.Data.Bom, settings);
        }
    }
}