﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query
{
    public class NorthwindMiscellaneousQuerySqliteTest : NorthwindMiscellaneousQueryTestBase<NorthwindQuerySqliteFixture<NoopModelCustomizer>>
    {
        // ReSharper disable once UnusedParameter.Local
        public NorthwindMiscellaneousQuerySqliteTest(NorthwindQuerySqliteFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override Task Query_expression_with_to_string_and_contains(bool async)
            => AssertTranslationFailed(() => base.Query_expression_with_to_string_and_contains(async));

        public override async Task Take_Skip(bool async)
        {
            await base.Take_Skip(async);

            AssertSql(
                @"@__p_0='10' (DbType = String)
@__p_1='5' (DbType = String)

SELECT ""t"".""CustomerID"", ""t"".""Address"", ""t"".""City"", ""t"".""CompanyName"", ""t"".""ContactName"", ""t"".""ContactTitle"", ""t"".""Country"", ""t"".""Fax"", ""t"".""Phone"", ""t"".""PostalCode"", ""t"".""Region""
FROM (
    SELECT ""c"".""CustomerID"", ""c"".""Address"", ""c"".""City"", ""c"".""CompanyName"", ""c"".""ContactName"", ""c"".""ContactTitle"", ""c"".""Country"", ""c"".""Fax"", ""c"".""Phone"", ""c"".""PostalCode"", ""c"".""Region""
    FROM ""Customers"" AS ""c""
    ORDER BY ""c"".""ContactName""
    LIMIT @__p_0
) AS ""t""
ORDER BY ""t"".""ContactName""
LIMIT -1 OFFSET @__p_1");
        }

        [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task Select_datetime_millisecond_component_composed(bool async)
        {
            await AssertQueryScalar(
                async,
                ss => ss.Set<Order>().Select(o => o.OrderDate.Value.AddYears(1).Millisecond));

            AssertSql(
                @"SELECT (CAST(strftime('%f', ""o"".""OrderDate"", CAST(1 AS TEXT) || ' years') AS REAL) * 1000.0) % 1000.0
FROM ""Orders"" AS ""o""");
        }

        [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task Select_datetime_TimeOfDay_component_composed(bool async)
        {
            await AssertQueryScalar(
                async,
                ss => ss.Set<Order>().Select(o => o.OrderDate.Value.AddYears(1).TimeOfDay));

            AssertSql(
                @"SELECT rtrim(rtrim(strftime('%H:%M:%f', ""o"".""OrderDate"", CAST(1 AS TEXT) || ' years'), '0'), '.')
FROM ""Orders"" AS ""o""");
        }

        public override async Task Select_expression_date_add_year(bool async)
        {
            await base.Select_expression_date_add_year(async);

            AssertSql(
                @"SELECT rtrim(rtrim(strftime('%Y-%m-%d %H:%M:%f', ""o"".""OrderDate"", CAST(1 AS TEXT) || ' years'), '0'), '.') AS ""OrderDate""
FROM ""Orders"" AS ""o""
WHERE ""o"".""OrderDate"" IS NOT NULL");
        }

        public override async Task Select_expression_datetime_add_month(bool async)
        {
            await base.Select_expression_datetime_add_month(async);

            AssertSql(
                @"SELECT rtrim(rtrim(strftime('%Y-%m-%d %H:%M:%f', ""o"".""OrderDate"", CAST(1 AS TEXT) || ' months'), '0'), '.') AS ""OrderDate""
FROM ""Orders"" AS ""o""
WHERE ""o"".""OrderDate"" IS NOT NULL");
        }

        public override async Task Select_expression_datetime_add_hour(bool async)
        {
            await base.Select_expression_datetime_add_hour(async);

            AssertSql(
                @"SELECT rtrim(rtrim(strftime('%Y-%m-%d %H:%M:%f', ""o"".""OrderDate"", CAST(1.0 AS TEXT) || ' hours'), '0'), '.') AS ""OrderDate""
FROM ""Orders"" AS ""o""
WHERE ""o"".""OrderDate"" IS NOT NULL");
        }

        public override async Task Select_expression_datetime_add_minute(bool async)
        {
            await base.Select_expression_datetime_add_minute(async);

            AssertSql(
                @"SELECT rtrim(rtrim(strftime('%Y-%m-%d %H:%M:%f', ""o"".""OrderDate"", CAST(1.0 AS TEXT) || ' minutes'), '0'), '.') AS ""OrderDate""
FROM ""Orders"" AS ""o""
WHERE ""o"".""OrderDate"" IS NOT NULL");
        }

        public override async Task Select_expression_datetime_add_second(bool async)
        {
            await base.Select_expression_datetime_add_second(async);

            AssertSql(
                @"SELECT rtrim(rtrim(strftime('%Y-%m-%d %H:%M:%f', ""o"".""OrderDate"", CAST(1.0 AS TEXT) || ' seconds'), '0'), '.') AS ""OrderDate""
FROM ""Orders"" AS ""o""
WHERE ""o"".""OrderDate"" IS NOT NULL");
        }

        public override async Task Select_expression_datetime_add_ticks(bool async)
        {
            await base.Select_expression_datetime_add_ticks(async);

            AssertSql(
                @"SELECT rtrim(rtrim(strftime('%Y-%m-%d %H:%M:%f', ""o"".""OrderDate"", CAST((10000 / 864000000000) AS TEXT) || ' seconds'), '0'), '.') AS ""OrderDate""
FROM ""Orders"" AS ""o""
WHERE ""o"".""OrderDate"" IS NOT NULL");
        }

        public override async Task Select_expression_date_add_milliseconds_above_the_range(bool async)
        {
            await base.Select_expression_date_add_milliseconds_above_the_range(async);

            AssertSql(
                @"SELECT rtrim(rtrim(strftime('%Y-%m-%d %H:%M:%f', ""o"".""OrderDate"", CAST((1000000000000.0 / 1000.0) AS TEXT) || ' seconds'), '0'), '.') AS ""OrderDate""
FROM ""Orders"" AS ""o""
WHERE ""o"".""OrderDate"" IS NOT NULL");
        }

        public override async Task Select_expression_date_add_milliseconds_below_the_range(bool async)
        {
            await base.Select_expression_date_add_milliseconds_below_the_range(async);

            AssertSql(
                @"SELECT rtrim(rtrim(strftime('%Y-%m-%d %H:%M:%f', ""o"".""OrderDate"", CAST((-1000000000000.0 / 1000.0) AS TEXT) || ' seconds'), '0'), '.') AS ""OrderDate""
FROM ""Orders"" AS ""o""
WHERE ""o"".""OrderDate"" IS NOT NULL");
        }

        public override async Task Select_expression_date_add_milliseconds_large_number_divided(bool async)
        {
            await base.Select_expression_date_add_milliseconds_large_number_divided(async);

            AssertSql(
                @"@__millisecondsPerDay_0='86400000' (DbType = String)

SELECT rtrim(rtrim(strftime('%Y-%m-%d %H:%M:%f', ""o"".""OrderDate"", CAST(CAST((CAST(((CAST(strftime('%f', ""o"".""OrderDate"") AS REAL) * 1000.0) % 1000.0) AS INTEGER) / @__millisecondsPerDay_0) AS REAL) AS TEXT) || ' days', CAST((CAST((CAST(((CAST(strftime('%f', ""o"".""OrderDate"") AS REAL) * 1000.0) % 1000.0) AS INTEGER) % @__millisecondsPerDay_0) AS REAL) / 1000.0) AS TEXT) || ' seconds'), '0'), '.') AS ""OrderDate""
FROM ""Orders"" AS ""o""
WHERE ""o"".""OrderDate"" IS NOT NULL");
        }

        public override async Task Select_distinct_long_count(bool async)
        {
            await base.Select_distinct_long_count(async);

            AssertSql(
                @"SELECT COUNT(*)
FROM (
    SELECT DISTINCT ""c"".""CustomerID"", ""c"".""Address"", ""c"".""City"", ""c"".""CompanyName"", ""c"".""ContactName"", ""c"".""ContactTitle"", ""c"".""Country"", ""c"".""Fax"", ""c"".""Phone"", ""c"".""PostalCode"", ""c"".""Region""
    FROM ""Customers"" AS ""c""
) AS ""t""");
        }

        public override async Task Select_orderBy_skip_long_count(bool async)
        {
            await base.Select_orderBy_skip_long_count(async);

            AssertSql(
                @"@__p_0='7' (DbType = String)

SELECT COUNT(*)
FROM (
    SELECT ""c"".""CustomerID"", ""c"".""Address"", ""c"".""City"", ""c"".""CompanyName"", ""c"".""ContactName"", ""c"".""ContactTitle"", ""c"".""Country"", ""c"".""Fax"", ""c"".""Phone"", ""c"".""PostalCode"", ""c"".""Region""
    FROM ""Customers"" AS ""c""
    ORDER BY ""c"".""Country""
    LIMIT -1 OFFSET @__p_0
) AS ""t""");
        }

        public override async Task Select_orderBy_take_long_count(bool async)
        {
            await base.Select_orderBy_take_long_count(async);

            AssertSql(
                @"@__p_0='7' (DbType = String)

SELECT COUNT(*)
FROM (
    SELECT ""c"".""CustomerID"", ""c"".""Address"", ""c"".""City"", ""c"".""CompanyName"", ""c"".""ContactName"", ""c"".""ContactTitle"", ""c"".""Country"", ""c"".""Fax"", ""c"".""Phone"", ""c"".""PostalCode"", ""c"".""Region""
    FROM ""Customers"" AS ""c""
    ORDER BY ""c"".""Country""
    LIMIT @__p_0
) AS ""t""");
        }

        public override async Task Select_skip_long_count(bool async)
        {
            await base.Select_skip_long_count(async);

            AssertSql(
                @"@__p_0='7' (DbType = String)

SELECT COUNT(*)
FROM (
    SELECT ""c"".""CustomerID"", ""c"".""Address"", ""c"".""City"", ""c"".""CompanyName"", ""c"".""ContactName"", ""c"".""ContactTitle"", ""c"".""Country"", ""c"".""Fax"", ""c"".""Phone"", ""c"".""PostalCode"", ""c"".""Region""
    FROM ""Customers"" AS ""c""
    ORDER BY (SELECT 1)
    LIMIT -1 OFFSET @__p_0
) AS ""t""");
        }

        public override async Task Select_take_long_count(bool async)
        {
            await base.Select_take_long_count(async);

            AssertSql(
                @"@__p_0='7' (DbType = String)

SELECT COUNT(*)
FROM (
    SELECT ""c"".""CustomerID"", ""c"".""Address"", ""c"".""City"", ""c"".""CompanyName"", ""c"".""ContactName"", ""c"".""ContactTitle"", ""c"".""Country"", ""c"".""Fax"", ""c"".""Phone"", ""c"".""PostalCode"", ""c"".""Region""
    FROM ""Customers"" AS ""c""
    LIMIT @__p_0
) AS ""t""");
        }

        public override Task Where_bitwise_binary_xor(bool async)
            => AssertTranslationFailed(() => base.Where_bitwise_binary_xor(async));

        public override async Task Where_shift_left_int(bool async)
        {
            await base.Where_shift_left_int(async);

            AssertSql(
                @"SELECT ""o"".""OrderID"", ""o"".""CustomerID"", ""o"".""EmployeeID"", ""o"".""OrderDate""
FROM ""Orders"" AS ""o""
WHERE (""o"".""OrderID"" << 1) = 20496");
        }

        public override async Task Where_shift_left_uint(bool async)
        {
            await base.Where_shift_left_uint(async);

            AssertSql(
                @"@__x_0='8' (DbType = String)

SELECT ""e"".""EmployeeID"", ""e"".""City"", ""e"".""Country"", ""e"".""FirstName"", ""e"".""ReportsTo"", ""e"".""Title""
FROM ""Employees"" AS ""e""
WHERE (@__x_0 << CAST(""e"".""EmployeeID"" AS INTEGER)) = 16");
        }

        public override async Task Where_shift_left_long(bool async)
        {
            await base.Where_shift_left_long(async);

            AssertSql(
                @"@__x_0='8' (DbType = String)

SELECT ""e"".""EmployeeID"", ""e"".""City"", ""e"".""Country"", ""e"".""FirstName"", ""e"".""ReportsTo"", ""e"".""Title""
FROM ""Employees"" AS ""e""
WHERE (@__x_0 << CAST(""e"".""EmployeeID"" AS INTEGER)) = 16");
        }

        public override async Task Where_shift_left_ulong(bool async)
        {
            await base.Where_shift_left_ulong(async);

            AssertSql(
                @"@__x_0='8' (DbType = String)

SELECT ""e"".""EmployeeID"", ""e"".""City"", ""e"".""Country"", ""e"".""FirstName"", ""e"".""ReportsTo"", ""e"".""Title""
FROM ""Employees"" AS ""e""
WHERE (@__x_0 << CAST(""e"".""EmployeeID"" AS INTEGER)) = 16");
        }

        public override async Task Where_shift_right_int(bool async)
        {
            await base.Where_shift_right_int(async);

            AssertSql(
                @"SELECT ""o"".""OrderID"", ""o"".""CustomerID"", ""o"".""EmployeeID"", ""o"".""OrderDate""
FROM ""Orders"" AS ""o""
WHERE (""o"".""OrderID"" >> 1) = 5124");
        }

        public override async Task Where_shift_right_uint(bool async)
        {
            await base.Where_shift_right_uint(async);

            AssertSql(
                @"@__x_0='8' (DbType = String)

SELECT ""e"".""EmployeeID"", ""e"".""City"", ""e"".""Country"", ""e"".""FirstName"", ""e"".""ReportsTo"", ""e"".""Title""
FROM ""Employees"" AS ""e""
WHERE (@__x_0 >> CAST(""e"".""EmployeeID"" AS INTEGER)) = 4");
        }

        public override async Task Where_shift_right_long(bool async)
        {
            await base.Where_shift_right_long(async);

            AssertSql(
                @"@__x_0='8' (DbType = String)

SELECT ""e"".""EmployeeID"", ""e"".""City"", ""e"".""Country"", ""e"".""FirstName"", ""e"".""ReportsTo"", ""e"".""Title""
FROM ""Employees"" AS ""e""
WHERE (@__x_0 >> CAST(""e"".""EmployeeID"" AS INTEGER)) = 4");
        }

        public override async Task Where_shift_right_ulong(bool async)
        {
            await base.Where_shift_right_ulong(async);

            AssertSql(
                @"@__x_0='8' (DbType = String)

SELECT ""e"".""EmployeeID"", ""e"".""City"", ""e"".""Country"", ""e"".""FirstName"", ""e"".""ReportsTo"", ""e"".""Title""
FROM ""Employees"" AS ""e""
WHERE (@__x_0 >> CAST(""e"".""EmployeeID"" AS INTEGER)) = 4");
        }

        public override Task Complex_nested_query_doesnt_try_binding_to_grandparent_when_parent_returns_complex_result(bool async)
            => null;

        public override Task SelectMany_correlated_subquery_hard(bool async) => null;

        public override Task AsQueryable_in_query_server_evals(bool async) => null;

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
