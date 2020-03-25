using CalculateFunding.Models.Calcs;
using CalculateFunding.Tests.Common.Helpers;

namespace CalculateFunding.Services.Calcs.Services
{
    public class CalculationBuilder : TestEntityBuilder
    {
        private string _id;
        private string _specificationId;
        private CalculationVersion _calculationVersion;

        public CalculationBuilder WithId(string id)
        {
            _id = id;

            return this;
        }

        public CalculationBuilder WithSpecificationId(string specificationId)
        {
            _specificationId = specificationId;

            return this;
        }

        public CalculationBuilder WithCurrentVersion(CalculationVersion calculationVersion)
        {
            _calculationVersion = calculationVersion;

            return this;
        }
        
        public Calculation Build()
        {
            return new Calculation
            {
                Id = _id ?? NewRandomString(),
                Current = _calculationVersion,
                SpecificationId = _specificationId ?? NewRandomString()
            };
        }
        
    }
}