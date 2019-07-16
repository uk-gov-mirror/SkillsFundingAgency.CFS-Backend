﻿using CalculateFunding.Common.ApiClient.Jobs.Models;

namespace CalculateFunding.Services.Publishing.UnitTests
{
    public class JobBuilder : TestEntityBuilder
    {
        private string _id;

        public JobBuilder WithId(string id)
        {
            _id = id;

            return this;
        }

        public Job Build()
        {
            return new Job
            {
                Id = _id ?? NewRandomString(),
            };
        }   
    }
}