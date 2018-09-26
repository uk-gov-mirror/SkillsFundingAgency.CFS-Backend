﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CalculateFunding.Models;
using CalculateFunding.Models.Datasets.Schema;
using CalculateFunding.Models.Results;
using CalculateFunding.Services.Calculator.Interfaces;
using Newtonsoft.Json;

namespace CalculateFunding.Services.Calculator
{
    public class AllocationModel : IAllocationModel
    {
        private readonly List<Tuple<MethodInfo, CalculationResult>> _methods = new List<Tuple<MethodInfo, CalculationResult>>();
        private readonly Dictionary<string, PropertyInfo> _datasetSetters = new Dictionary<string, PropertyInfo>();
        private readonly object _instance;
        private readonly object _datasetsInstance;
        private readonly object _providerInstance;

        public AllocationModel(Type allocationType, Dictionary<string, Type> datasetTypes)
        {
            DatasetTypes = datasetTypes;
            PropertyInfo datasetsSetter = allocationType.GetProperty("Datasets");
            Type datasetType = datasetsSetter.PropertyType;
            foreach (var relationshipProperty in datasetType.GetProperties().Where(x => x.CanWrite).ToArray())
            {
                var relationshipAttribute = relationshipProperty.GetCustomAttributesData()
                    .FirstOrDefault(x => x.AttributeType.Name == "DatasetRelationshipAttribute");
                if (relationshipAttribute != null)
                {
                    _datasetSetters.Add(GetProperty(relationshipAttribute, "Name"), relationshipProperty);
                }
            }

            PropertyInfo providerSetter = allocationType.GetProperty("Provider");
            Type providerType = providerSetter.PropertyType;

            var executeMethods = allocationType.GetMethods().Where(x => x.ReturnType == typeof(decimal));
            foreach (var executeMethod in executeMethods)
            {
                var parameters = executeMethod.GetParameters();

                var attributes = executeMethod.GetCustomAttributesData();
                var calcAttribute = attributes.FirstOrDefault(x => x.AttributeType.Name == "CalculationAttribute");
                if (calcAttribute != null)
                {
                    var result = new CalculationResult
                    {
                        Calculation = GetReference(attributes, "Calculation"),
                        CalculationSpecification = GetReference(attributes, "CalculationSpecification"),
                        AllocationLine = GetReference(attributes, "AllocationLine"),
                        PolicySpecifications = GetReferences(attributes, "PolicySpecification").ToList()
                    };

                    if (parameters.Length == 0)
                    {
                        _methods.Add(new Tuple<MethodInfo, CalculationResult>(executeMethod, result));
                    }
                }
            }

            _instance = Activator.CreateInstance(allocationType);
            _datasetsInstance = Activator.CreateInstance(datasetType);
            datasetsSetter.SetValue(_instance, _datasetsInstance);
            _providerInstance = Activator.CreateInstance(providerType);
        }

        public object Instance
        {
            get
            {
                return _instance;
            }
        }

        public Type GetDatasetType(string datasetName)
        {
            if (DatasetTypes.ContainsKey(datasetName))
            {
                return DatasetTypes[datasetName];
            }
            throw new NotImplementedException($"{datasetName} is not defined");
        }

        public object CreateDataset(string datasetName)
        {
            if (DatasetTypes.ContainsKey(datasetName))
            {
                try
                {
                    var type = DatasetTypes[datasetName];
                    return Activator.CreateInstance(type);
                }
                catch (ReflectionTypeLoadException e)
                {
                    throw new Exception(string.Join(", ", e.LoaderExceptions.Select(x => x.Message)));
                }
            }
            throw new NotImplementedException($"{datasetName} is not defined");
        }

        private Dictionary<string, Type> DatasetTypes { get; }

        public IEnumerable<CalculationResult> Execute(List<ProviderSourceDataset> datasets, ProviderSummary providerSummary)
        {
            var datasetNamesUsed = new HashSet<string>();
            foreach (var dataset in datasets)
            {
                var type = GetDatasetType(dataset.DataDefinition.Name);

                if (_datasetSetters.TryGetValue(dataset.DataRelationship.Name, out var setter))
                {
                    datasetNamesUsed.Add(dataset.DataRelationship.Name);
                    if (dataset.DataGranularity == DataGranularity.SingleRowPerProvider)
                    {
                        var row = PopulateRow(type, dataset.Current.Rows.First());
                        setter.SetValue(_datasetsInstance, row);
                    }
                    else
                    {
                        Type constructGeneric = typeof(List<>).MakeGenericType(type);
                        var list = Activator.CreateInstance(constructGeneric);
                        var addMethod = list.GetType().GetMethod("Add");
                        var itemType = list.GetType().GenericTypeArguments.First();
                        var rows = dataset.Current.Rows.Select(x => PopulateRow(itemType, x)).ToArray();
                        foreach (var row in rows)
                        {
                            addMethod.Invoke(list, new[] { row });
                        }

                        setter.SetValue(_datasetsInstance, list);
                    }
                }
            }

            PropertyInfo providerSetter = _instance.GetType().GetProperty("Provider"); 

            object provider = PopulateProvider(providerSummary, providerSetter);
            providerSetter.SetValue(_instance, provider);
           
            // Add default object for any missing datasets to help reduce null exceptions
            foreach (var key in _datasetSetters.Keys.Where(x => !datasetNamesUsed.Contains(x)))
            {
                if (_datasetSetters.TryGetValue(key, out var setter))
                {
                    setter.SetValue(_datasetsInstance, Activator.CreateInstance(setter.PropertyType));
                }
            }
        
            foreach (var executeMethod in _methods)
            {
                var result = executeMethod.Item2;
                try
                {
                    result.Value = (decimal)executeMethod.Item1.Invoke(_instance, null);
                }
                catch (Exception e)
                {
                    result.Exception = e;
                }
                yield return result;
            }
        }

        private object PopulateProvider(ProviderSummary providerSummary, PropertyInfo providerSetter)
        {
            Type type = providerSetter.PropertyType;

            object data = Activator.CreateInstance(type);

            foreach (var property in type.GetProperties().Where(x => x.CanWrite).ToArray())
            {
                
                switch (property.Name)
                {
                    case "DateOpened":
                        property.SetValue(data, providerSummary.DateOpened.HasValue ? providerSummary.DateOpened.Value.Date : (DateTime?)null);
                        break;
                    case "ProviderType":
                        property.SetValue(data, providerSummary.ProviderType.EmptyIfNull());
                        break;
                    case "ProviderSubType":
                        property.SetValue(data, providerSummary.ProviderSubType.EmptyIfNull());
                        break;
                    case "Name":
                        property.SetValue(data, providerSummary.Name.EmptyIfNull());
                        break;
                    case "UKPRN":
                        property.SetValue(data, providerSummary.UKPRN.EmptyIfNull());
                        break;
                    case "URN":
                        property.SetValue(data, providerSummary.URN.EmptyIfNull());
                        break;
                    case "UPIN":
                        property.SetValue(data, providerSummary.UPIN.EmptyIfNull());
                        break;
                    case "DfeEstablishmentNumber":
                        property.SetValue(data, providerSummary.DfeEstablishmentNumber.EmptyIfNull());
                        break;
                    case "EstablishmentNumber":
                        property.SetValue(data, providerSummary.EstablishmentNumber.EmptyIfNull());
                        break;
                    case "LegalName":
                        property.SetValue(data, providerSummary.LegalName.EmptyIfNull());
                        break;
                    case "Authority":
                        property.SetValue(data, providerSummary.Authority.EmptyIfNull());
                        break;
                    case "DateClosed":
                        property.SetValue(data, providerSummary.DateClosed.HasValue ? providerSummary.DateClosed.Value.Date : (DateTime?)null);
                        break;
                    case "LACode":
                        property.SetValue(data, providerSummary.LACode.EmptyIfNull());
                        break;
                    case "CrmAccountId":
                        property.SetValue(data, providerSummary.CrmAccountId.EmptyIfNull());
                        break;
                    case "NavVendorNo":
                        property.SetValue(data, providerSummary.NavVendorNo.EmptyIfNull());
                        break;
                    case "Status":
                        property.SetValue(data, providerSummary.Status.EmptyIfNull());
                        break;
                    case "PhaseOfEducation":
                        property.SetValue(data, providerSummary.PhaseOfEducation.EmptyIfNull());
                        break;
                    default:
                        break;
                }
            }

            return data;
        }

        private object PopulateRow(Type type, Dictionary<string, object> row)
        {
            var data = Activator.CreateInstance(type);
            foreach (var property in type.GetProperties().Where(x => x.CanWrite).ToArray())
            {
                var fieldAttribute = property.GetCustomAttributesData()
                    .FirstOrDefault(x => x.AttributeType.Name == "FieldAttribute");
                if (fieldAttribute != null)
                {
                    string propertyName = GetProperty(fieldAttribute, "Name");

                    if (row.TryGetValue(propertyName, out var value))
                    {
                        var propType = property.PropertyType.ToString();

                        if (propType == "System.Decimal")
                        {
                            value = Convert.ToDecimal(value);
                        }

                        if (propType == "System.Int32")
                        {
                            value = Convert.ToInt32(value);
                        }

                        if (propType == "System.Int64")
                        {
                            value = Convert.ToInt64(value);
                        }

                        property.SetValue(data, value);
                    }
                }
            }
            return data;
        }

        private static IEnumerable<Reference> GetReferences(IList<CustomAttributeData> attributes, string attributeName)
        {
            foreach (var attribute in attributes.Where(x => x.AttributeType.Name.StartsWith(attributeName)))
            {
                yield return new Reference(GetProperty(attribute, "Id"), GetProperty(attribute, "Name"));
            }
        }

        private static Reference GetReference(IList<CustomAttributeData> attributes, string attributeName)
        {
            var attribute = attributes.FirstOrDefault(x => x.AttributeType.Name.StartsWith(attributeName));
            if (attribute != null)
            {
                return new Reference(GetProperty(attribute, "Id"), GetProperty(attribute, "Name"));
            }
            return null;
        }

        private static string GetProperty(CustomAttributeData attribute, string propertyName)
        {
            var argument = attribute.NamedArguments.FirstOrDefault(x => x.MemberName == propertyName);
            if (argument != null)
            {
                return argument.TypedValue.Value?.ToString();
            }
            return null;
        }
    }
}