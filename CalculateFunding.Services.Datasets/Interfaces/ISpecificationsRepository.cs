﻿using CalculateFunding.Models.Results;
using CalculateFunding.Models.Specs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CalculateFunding.Services.Datasets.Interfaces
{
    public interface ISpecificationsRepository
    {
        Task<Specification> GetSpecificationById(string specificationId);
    }
}
