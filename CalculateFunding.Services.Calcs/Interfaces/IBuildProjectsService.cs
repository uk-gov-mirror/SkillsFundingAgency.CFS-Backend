﻿using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;

namespace CalculateFunding.Services.Calcs.Interfaces
{
    public interface IBuildProjectsService
    {
        Task GenerateAllocationsInstruction(EventData message);
        Task UpdateAllocations(EventData message);
    }
}
