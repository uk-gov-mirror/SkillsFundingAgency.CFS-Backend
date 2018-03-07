﻿using AutoMapper;
using CalculateFunding.Models.Specs;
using System;
using System.Collections.Generic;
using System.Text;

namespace CalculateFunding.Models.MappingProfiles
{
    public class SpecificationsMappingProfile : Profile
    {
        public SpecificationsMappingProfile()
        {
            CreateMap<SpecificationCreateModel, Specification>()
                .AfterMap((src, dest) => { dest.Id = Guid.NewGuid().ToString(); })
                .ForMember(m => m.Id, opt => opt.Ignore())
                .ForMember(m => m.AcademicYear, opt => opt.Ignore())
                .ForMember(m => m.Policies, opt => opt.Ignore())
                .ForMember(m => m.FundingStream, opt => opt.Ignore())
                .ForMember(m => m.DataDefinitionRelationshipIds, opt => opt.Ignore());

            CreateMap<PolicyCreateModel, Policy>()
                .AfterMap((src, dest) => { dest.Id = Guid.NewGuid().ToString(); })
                .ForMember(m => m.Id, opt => opt.Ignore())
                .ForMember(m => m.Calculations, opt => opt.Ignore())
                .ForMember(m => m.SubPolicies, opt => opt.Ignore());

            CreateMap<CalculationCreateModel, Calculation>()
                .AfterMap((src, dest) => { dest.Id = Guid.NewGuid().ToString(); })
                .ForMember(m => m.Id, opt => opt.Ignore())
                .ForMember(m => m.AllocationLine, opt => opt.Ignore());
        }
    }
}
