using System.Threading.Tasks;
using CalculateFunding.Models.Publishing;

namespace CalculateFunding.Services.Publishing.Interfaces
{
    public interface IPublishedProviderVersionErrorDetectionPipeline
    {
        Task ProcessPublishedProvider(PublishedProviderVersion publishedProviderVersion);
    }
}