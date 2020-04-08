using System.Collections.Generic;
using System.Linq;
using CalculateFunding.Common.CosmosDb;
using CalculateFunding.Services.Publishing.Interfaces;

namespace CalculateFunding.Services.Publishing
{
    public class PublishedFundingQueryBuilder : IPublishedFundingQueryBuilder
    {
        public CosmosDbQuery BuildCountQuery(IEnumerable<string> fundingStreamIds,
            IEnumerable<string> fundingPeriodIds,
            IEnumerable<string> groupingReasons)
        {
            return new CosmosDbQuery
            {
                QueryText = $@"
                SELECT
                   VALUE COUNT(1)
                {BuildFromClauseAndPredicates(fundingStreamIds, fundingPeriodIds, groupingReasons)}"
            };
        }

        public CosmosDbQuery BuildQuery(
            IEnumerable<string> fundingStreamIds,
            IEnumerable<string> fundingPeriodIds,
            IEnumerable<string> groupingReasons,
            int top,
            int? pageRef)
        {
            return new CosmosDbQuery
            {
                QueryText = $@"
                SELECT
                    p.content.id,
                    p.content.statusChangedDate,
                    p.content.fundingStreamId,
                    p.content.fundingPeriod.id AS FundingPeriodId,
                    p.content.groupingReason AS GroupingType,
                    p.content.organisationGroupTypeCode AS GroupTypeIdentifier,
                    p.content.organisationGroupIdentifierValue AS IdentifierValue,
                    p.content.version,
                    CONCAT(p.content.fundingStreamId, '-', 
                            p.content.fundingPeriod.id, '-',
                            p.content.groupingReason, '-',
                            p.content.organisationGroupTypeCode, '-',
                            ToString(p.content.organisationGroupIdentifierValue), '-',
                            ToString(p.content.majorVersion), '_',
                            ToString(p.content.minorVersion), '.json')
                    AS DocumentPath,
                    p.deleted
                {BuildFromClauseAndPredicates(fundingStreamIds, fundingPeriodIds, groupingReasons)}
                ORDER BY p.documentType,
				p.content.statusChangedDate, 
				p.content.id,
				p.content.fundingStreamId,
				p.content.fundingPeriod.id,
				p.content.groupingReason,
				p.deleted
                {PagingSkipLimit(pageRef, top)}"
            };
        }

        private string BuildFromClauseAndPredicates(IEnumerable<string> fundingStreamIds,
            IEnumerable<string> fundingPeriodIds,
            IEnumerable<string> groupingReasons)
        {
            return $@"FROM publishedFunding p
                WHERE p.documentType = 'PublishedFundingVersion'
                AND p.deleted = false
                {FundingStreamsPredicate(fundingStreamIds)}
                {FundingPeriodsPredicate(fundingPeriodIds)}
                {GroupingReasonsPredicate(groupingReasons)}";
        }

        private string FundingStreamsPredicate(IEnumerable<string> fundingStreamIds)
            => InPredicateFor(fundingStreamIds, "p.content.fundingStreamId");

        private string FundingPeriodsPredicate(IEnumerable<string> fundingPeriodIds)
            => InPredicateFor(fundingPeriodIds, "p.content.fundingPeriod.id");

        private string GroupingReasonsPredicate(IEnumerable<string> groupingReasons)
            => InPredicateFor(groupingReasons, "p.content.groupingReason");

        private string InPredicateFor(IEnumerable<string> matches, string field)
        {
            return !matches.IsNullOrEmpty() ?
                $"AND {field} IN ({string.Join(",", matches.Select(_ => $"'{_}'"))})" :
                null;
        }

        private string PagingSkipLimit(int? pageRef, int top)
        {
            return pageRef.HasValue ?
                $"OFFSET {(pageRef - 1) * top} LIMIT {top}" :
                $"LIMIT {top}";
        }
    }
}