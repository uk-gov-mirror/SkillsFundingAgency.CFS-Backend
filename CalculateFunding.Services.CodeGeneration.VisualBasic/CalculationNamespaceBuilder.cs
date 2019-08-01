using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CalculateFunding.Models.Calcs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace CalculateFunding.Services.CodeGeneration.VisualBasic
{
    public class CalculationNamespaceBuilder : VisualBasicTypeGenerator
    {
        private readonly CompilerOptions _compilerOptions;

        public CalculationNamespaceBuilder(CompilerOptions compilerOptions)
        {
            _compilerOptions = compilerOptions;
        }

        public NamespaceBuilderResult BuildNamespacesForCalculations(IEnumerable<Calculation> calculations)
        {
            NamespaceBuilderResult result = new NamespaceBuilderResult();

            IEnumerable<IGrouping<string, Calculation>> fundingStreamCalculationGroups
                = calculations.Where(_ => _.Current.Namespace == CalculationNamespace.Template)
                    .GroupBy(_ => _.FundingStreamId)
                    .ToArray();
            IEnumerable<string> fundingStreamNamespaces = fundingStreamCalculationGroups.Select(_ => _.Key).ToArray();
            IEnumerable<Calculation> additionalCalculations = calculations.Where(_ => _.Current.Namespace == CalculationNamespace.Additional)
                .ToArray();

            IEnumerable<string> propertyAssignments = CreatePropertyAssignments(fundingStreamNamespaces);
            IEnumerable<StatementSyntax> propertyDefinitions = CreateProperties(fundingStreamNamespaces);

            result.PropertiesDefinitions = propertyDefinitions.ToArray();

            foreach (IGrouping<string, Calculation> fundingStreamCalculationGroup in fundingStreamCalculationGroups)
                result.InnerClasses.Add(CreateNamespaceDefinition(fundingStreamCalculationGroup.Key,
                    fundingStreamCalculationGroup,
                    propertyDefinitions,
                    propertyAssignments));

            result.InnerClasses.Add(CreateNamespaceDefinition("Calculations",
                additionalCalculations,
                propertyDefinitions,
                propertyAssignments,
                "AdditionalCalculations"));

            return result;
        }

        private NamespaceClassDefinition CreateNamespaceDefinition(
            string @namespace,
            IEnumerable<Calculation> calculationsInNamespace,
            IEnumerable<StatementSyntax> propertyDefinitions,
            IEnumerable<string> propertyAssignments,
            string className = null)
        {
            ClassStatementSyntax classStatement = SyntaxFactory
                .ClassStatement(GenerateIdentifier(className ?? $"{@namespace}Calculations"))
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)));

            SyntaxList<InheritsStatementSyntax> inherits = SyntaxFactory.SingletonList(SyntaxFactory.InheritsStatement(_compilerOptions.UseLegacyCode
                ? SyntaxFactory.ParseTypeName("LegacyBaseCalculation")
                : SyntaxFactory.ParseTypeName("BaseCalculation")));

            IEnumerable<StatementSyntax> namespaceFunctionPointers = CreateNamespaceFunctionPointers(calculationsInNamespace);

            StatementSyntax initialiseMethodDefinition = CreateInitialiseMethod(calculationsInNamespace,
                propertyAssignments);

            ClassBlockSyntax classBlock = SyntaxFactory.ClassBlock(classStatement,
                inherits,
                new SyntaxList<ImplementsStatementSyntax>(),
                SyntaxFactory.List(propertyDefinitions
                    .Concat(namespaceFunctionPointers)
                    .Concat(new[]
                    {
                        initialiseMethodDefinition
                    }).ToArray()),
                SyntaxFactory.EndClassStatement());

            return new NamespaceClassDefinition(@namespace, classBlock);
        }

        private static IEnumerable<string> CreatePropertyAssignments(IEnumerable<string> namespaces)
        {
            return namespaces.Select(@namespace => $"{@namespace} = calculationContext.{@namespace}")
                .Concat(new[]
                {
                    "Calculations = calculationContext.Calculations"
                })
                .ToArray();
        }

        private static IEnumerable<StatementSyntax> CreateProperties(IEnumerable<string> namespaces)
        {
            yield return CreateProperty("Provider");
            yield return CreateProperty("Datasets");
            yield return CreateProperty("Calculations", "AdditionalCalculations");

            foreach (var @namespace in namespaces) yield return CreateProperty(@namespace, $"{@namespace}Calculations");
        }

        private static IEnumerable<StatementSyntax> CreateNamespaceFunctionPointers(IEnumerable<Calculation> calculations)
        {
            foreach (Calculation calculation in calculations)
            {
                StringBuilder sourceCode = new StringBuilder();

                CalculationVersion currentCalculationVersion = calculation.Current;
                
                if (string.IsNullOrWhiteSpace(currentCalculationVersion.SourceCodeName)) throw new InvalidOperationException($"Calculation source code name is not populated for calc {calculation.Id}");

                // Add attributes to describe calculation and calculation specification
                sourceCode.AppendLine($"<Calculation(Id := \"{calculation.Id}\", Name := \"{calculation.Name}\")>");
               
                // Add attribute for calculation description
                if (currentCalculationVersion.Description.IsNotNullOrWhitespace()) sourceCode.AppendLine($"<Description(Description := \"{currentCalculationVersion.Description?.Replace("\"", "\"\"")}\")>");

                sourceCode.AppendLine($"Public {currentCalculationVersion.SourceCodeName} As Func(Of decimal?) = Nothing");
                sourceCode.AppendLine();

                yield return ParseSourceCodeToStatementSyntax(sourceCode);
            }
        }

        private StatementSyntax CreateInitialiseMethod(IEnumerable<Calculation> calculations,
            IEnumerable<string> propertyAssignments)
        {
            StringBuilder sourceCode = new StringBuilder();

            sourceCode.AppendLine();
            sourceCode.AppendLine("Public Sub Initialise(calculationContext As CalculationContext)");
            sourceCode.AppendLine("Datasets = calculationContext.Datasets");
            sourceCode.AppendLine("Provider = calculationContext.Provider");
            sourceCode.AppendLine();

            foreach (var propertyAssignment in propertyAssignments) sourceCode.AppendLine(propertyAssignment);

            sourceCode.AppendLine();

            foreach (Calculation calculation in calculations)
            {
                if (!string.IsNullOrWhiteSpace(calculation.Current?.SourceCode)) calculation.Current.SourceCode = QuoteAggregateFunctionCalls(calculation.Current.SourceCode);

                sourceCode.AppendLine();

                sourceCode.AppendLine($"{calculation.Current.SourceCodeName} = Function() As decimal?");
                sourceCode.AppendLine("Dim existingCacheItem as String() = Nothing");
                sourceCode.AppendLine($"If calculationContext.Dictionary.TryGetValue(\"{calculation.Id}\", existingCacheItem) Then");
                sourceCode.AppendLine("Dim existingCalculationResultDecimal As Decimal? = Nothing");
                sourceCode.AppendLine($"   If calculationContext.DictionaryValues.TryGetValue(\"{calculation.Id}\", existingCalculationResultDecimal) Then");
                sourceCode.AppendLine("        Return existingCalculationResultDecimal");
                sourceCode.AppendLine("    End If");

                sourceCode.AppendLine("    If existingCacheItem.Length > 2 Then");
                sourceCode.AppendLine("       Dim exceptionType as String = existingCacheItem(1)");
                sourceCode.AppendLine("        If Not String.IsNullOrEmpty(exceptionType) then");
                sourceCode.AppendLine("            Dim exceptionMessage as String = existingCacheItem(2)");
                sourceCode.AppendLine(
                    $"           Throw New ReferencedCalculationFailedException(\"{calculation.Name} failed due to exception type:\" + exceptionType  + \" with message: \" + exceptionMessage)");
                sourceCode.AppendLine("        End If");
                sourceCode.AppendLine("    End If");
                sourceCode.AppendLine("End If");
                sourceCode.AppendLine("Dim userCalculationCodeImplementation As Func(Of Decimal?) = Function() as Decimal?");
                sourceCode.AppendLine("Dim frameCount = New System.Diagnostics.StackTrace().FrameCount");
                sourceCode.AppendLine("If frameCount > calculationContext.StackFrameStartingCount + 40 Then");
                sourceCode.AppendLine(
                    $"   Throw New CalculationStackOverflowException(\"The system detected a stackoverflow from {calculation.Name}, this is probably due to recursive methods stuck in an infinite loop\")");
                sourceCode.AppendLine("End If");

                sourceCode.AppendLine($"#ExternalSource(\"{calculation.Id}|{calculation.Name}\", 1)");
                sourceCode.AppendLine();
                sourceCode.Append(calculation.Current?.SourceCode ?? CodeGenerationConstants.VisualBasicDefaultSourceCode);
                sourceCode.AppendLine();
                sourceCode.AppendLine("#End ExternalSource");

                sourceCode.AppendLine("End Function");
                sourceCode.AppendLine();

                sourceCode.AppendLine("Try");
                sourceCode.AppendLine("Dim executedUserCodeCalculationResult As Nullable(Of Decimal) = userCalculationCodeImplementation()");
                sourceCode.AppendLine();
                sourceCode.AppendLine(
                    $"calculationContext.Dictionary.Add(\"{calculation.Id}\", {{If(executedUserCodeCalculationResult.HasValue, executedUserCodeCalculationResult.ToString(), \"\")}})");
                sourceCode.AppendLine($"calculationContext.DictionaryValues.Add(\"{calculation.Id}\", executedUserCodeCalculationResult)");
                sourceCode.AppendLine("Return executedUserCodeCalculationResult");
                sourceCode.AppendLine("Catch ex as System.Exception");
                sourceCode.AppendLine($"   calculationContext.Dictionary.Add(\"{calculation.Id}\", {{\"\", ex.GetType().Name, ex.Message}})");
                sourceCode.AppendLine("    Throw");
                sourceCode.AppendLine("End Try");
                sourceCode.AppendLine();

                sourceCode.AppendLine("End Function");
            }

            sourceCode.AppendLine();
            sourceCode.AppendLine("End Sub");

            return ParseSourceCodeToStatementSyntax(sourceCode);
        }
    }
}