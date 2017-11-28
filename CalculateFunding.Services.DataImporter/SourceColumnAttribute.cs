﻿using System;

namespace CalculateFunding.Services.DataImporter
{
    public class SourceColumnAttribute : Attribute
    {
        public string ColumnName { get; }

        public SourceColumnAttribute(string columnName)
        {
            ColumnName = columnName;
        }
    }
}