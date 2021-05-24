// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Xunit;

namespace Microsoft.EntityFrameworkCore
{
    public class LinqExpressionToCSharpTranslatorTest
    {
        #region Member access

        [Fact]
        public void Member_access_property()
            => Test(d => d.IntProperty == 3, "d => d.IntProperty == 3");

        [Fact]
        public void Member_access_field()
            => Test(d => d.IntField == 3, "d => d.IntField == 3");

        #endregion Member access

        #region Binary

        [Fact]
        public void Binary_equal()
            => Test(d => d.IntProperty != 3, "d => d.IntProperty != 3");

        [Fact]
        public void Binary_not_equal()
            => Test(d => d.IntProperty != 3, "d => d.IntProperty != 3");

        [Fact]
        public void Binary_and()
            => Test(d => d.IntProperty == 3 && d.StringProperty == "foo", @"d => d.IntProperty == 3 && d.StringProperty == ""foo""");

        [Fact]
        public void Binary_greater_than()
            => Test(d => d.IntProperty > 3, "d => d.IntProperty > 3");

        [Fact]
        public void Binary_less_than()
            => Test(d => d.IntProperty < 3, "d => d.IntProperty < 3");

        [Fact]
        public void Binary_greater_than_or_equal()
            => Test(d => d.IntProperty >= 3, "d => d.IntProperty >= 3");

        [Fact]
        public void Binary_less_than_or_equal()
            => Test(d => d.IntProperty <= 3, "d => d.IntProperty <= 3");

        #endregion Binary

        #region Literal

        [Fact]
        public void Literal_int()
            => Test(d => d.IntProperty == 3, "d => d.IntProperty == 3");

        [Fact]
        public void Literal_string()
            => Test(d => d.StringProperty == "foo", @"d => d.StringProperty == ""foo""");

        [Fact]
        public void Literal_null()
            => Test(d => d == null, "d => d == null");

        #endregion Literal

        [Fact]
        public void Block_empty()
            => Test(Expression.Block(), @"{
}");

        [Fact]
        public void Block_with_variables()
        {
            var variable = Expression.Variable(typeof(int), "v");

            var block = Expression.Block(new[] { variable }, new Expression[0]);

            Test(block, @"{
    var v;
}");
        }

        [Fact]
        public void Assign()
        {
            var variable = Expression.Variable(typeof(int), "v");

            var block = Expression.Block(
                new[] { variable },
                Expression.Assign(variable, Expression.Constant(3)));

            Test(block, @"{
    var v;
    v = 3;
}");
        }

        [Fact]
        public void New_without_args()
            => Test(Expression.Block(Expression.New(typeof(object))), @"{
    new Object();
}");

        [Fact]
        public void New_with_args()
        {
            var constructor = typeof(StringBuilder).GetConstructor(new[] { typeof(string) });

            Test(Expression.Block(
                Expression.New(constructor, Expression.Constant("foo"))), @"{
    new StringBuilder(""foo"");
}");
        }

        [Fact]
        public void New_generic()
            => Test(Expression.Block(Expression.New(typeof(List<string>))), @"{
    new List<String>();
}");

        #region Support

        protected void Test(Expression<Func<Dummy, bool>> expression, string expected)
            => Test((Expression)expression, expected);

        protected void Test(Expression expression, string expected)
        {
            var translator = new LinqExpressionToCSharpTranslator();
            var actual = translator.TranslateAndSerialize(expression);
            Assert.Equal(expected, actual, ignoreLineEndingDifferences: true);
        }

        public class Dummy
        {
            public int IntField;
            public int IntProperty { get; set; }
            public string StringProperty { get; set; }
        }

        #endregion Support
    }
}
