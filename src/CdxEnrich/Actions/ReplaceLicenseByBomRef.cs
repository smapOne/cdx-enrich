using CdxEnrich.Config;
using CdxEnrich.FunctionalHelpers;
using CycloneDX.Models;

namespace CdxEnrich.Actions
{
    public class ReplaceLicenseByBomRef : ReplaceAction
    {
        static readonly string moduleName = nameof(ReplaceLicenseByBomRef);

        private static Component? GetComponentByBomRef(Bom bom, string bomRef)
        {
            return
                bom.Components.Find(comp => comp.BomRef == bomRef);
        }

        private static Result<ConfigRoot> MustNotHaveMoreThanOnePropertySet(ConfigRoot config)
        {
            var hasInvalidEntries = config.ReplaceLicenseByBomRef?
                .Exists(rec => new object?[] { rec.Id, rec.Name, rec.Expression }.Count(x => x != null) > 1) == true;

            if (hasInvalidEntries)
            {
                return InvalidConfigError.Create<ConfigRoot>(moduleName, "One entry must have either Id or Name or Expression. Not more than one.");
            }
            else
            {
                return new Ok<ConfigRoot>(config);
            }
        }

        private static Result<ConfigRoot> MustHaveEitherIdOrNameOrExpression(ConfigRoot config)
        {
            if (config.ReplaceLicenseByBomRef?.Exists(rec => rec.Name == null && rec.Id == null && rec.Expression == null) == true)
            {
                return InvalidConfigError.Create<ConfigRoot>(moduleName, "One entry must have either Id, Name or Expression.");
            }
            else
            {
                return new Ok<ConfigRoot>(config);
            }
        }

        private static Result<ConfigRoot> BomRefMustNotBeNullOrEmpty(ConfigRoot config)
        {
            if (config.ReplaceLicenseByBomRef?.Exists(rec => string.IsNullOrEmpty(rec.Ref)) == true)
            {
                return InvalidConfigError.Create<ConfigRoot>(moduleName, "BomRef must be set and cannot be an emtpy string.");
            }
            else
            {
                return new Ok<ConfigRoot>(config);
            }
        }

        public override Result<ConfigRoot> CheckConfig(ConfigRoot config)
        {
            return
                MustHaveEitherIdOrNameOrExpression(config)
                .Bind(MustNotHaveMoreThanOnePropertySet)
                .Bind(BomRefMustNotBeNullOrEmpty);
        }

        public override InputTuple Execute(InputTuple inputs)
        {
            inputs.Config.ReplaceLicenseByBomRef?
                   .Where(rep => rep.Ref != null)
                   .ToList()
                   .ForEach(rep =>
                   {
                       var comp = GetComponentByBomRef(inputs.Bom, rep.Ref!);
                       if (comp != null)
                       {
                           comp.Licenses =
                               [
                                   CreateLicenseChoice(rep)
                               ];
                       }
                   });

            return inputs;
        }
        
        private LicenseChoice CreateLicenseChoice(ReplaceLicenseByBomRefConfig rep)
        {
            if (rep.Expression != null)
            {
                return new LicenseChoice
                {
                    Expression = rep.Expression,
                };
            }

            return new LicenseChoice
            {
                License = new License
                {
                    Name = rep.Name,
                    Id = rep.Id
                }
            };
        }
    }
}