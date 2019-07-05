﻿using AutoMapper;
using CalculateFunding.Models.Providers;
using CalculateFunding.Models.Providers.ViewModels;
using CommonModels = CalculateFunding.Common.ApiClient.Providers.Models;

namespace CalculateFunding.Models.MappingProfiles
{
    public class ProviderVersionsMappingProfile : Profile
    {
        private const string MASTER_KEY = "master";

        public ProviderVersionsMappingProfile()
        {
            CreateMap<MasterProviderVersionViewModel, MasterProviderVersion>()
                .ForMember(c => c.ProviderVersionTypeString, opt => opt.Ignore())
                .ForMember(c => c.Name, opt => opt.Ignore())
                .ForMember(c => c.Description, opt => opt.Ignore())
                .ForMember(c => c.Version, opt => opt.Ignore())
                .ForMember(c => c.TargetDate, opt => opt.Ignore())
                .ForMember(c => c.FundingStream, opt => opt.Ignore())
                .ForMember(c => c.VersionType, opt => opt.Ignore())
                .ForMember(c => c.Created, opt => opt.Ignore())
                .ForMember(c => c.ProviderVersionId, opt => opt.MapFrom(c => c.ProviderVersionId))
                .ForMember(c => c.Id, opt => opt.MapFrom(s => MASTER_KEY));

            CreateMap<ProviderVersionViewModel, ProviderVersion>()
                .ForMember(c => c.ProviderVersionTypeString, opt => opt.Ignore())
                .ForMember(c => c.ProviderVersionId, opt => opt.MapFrom(c => c.ProviderVersionId))
                .ForMember(c => c.Providers, opt => opt.MapFrom(c => c.Providers))
                .ForMember(c => c.Id, opt => opt.MapFrom(s => MASTER_KEY));

            CreateMap<Results.ProviderSummary, CommonModels.ProviderSummary>();
        }
    }
}
