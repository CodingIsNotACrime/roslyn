﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Roslyn.Utilities;
using System.Collections.Generic;
using System;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal sealed class UsingStatementBinder : LockOrUsingBinder
    {
        private readonly UsingStatementSyntax _syntax;

        public UsingStatementBinder(Binder enclosing, UsingStatementSyntax syntax)
            : base(enclosing)
        {
            _syntax = syntax;
        }

        override protected ImmutableArray<LocalSymbol> BuildLocals()
        {
            if (_syntax.Declaration != null)
            {
                var locals = new ArrayBuilder<LocalSymbol>(_syntax.Declaration.Variables.Count);
                foreach (VariableDeclaratorSyntax declarator in _syntax.Declaration.Variables)
                {
                    locals.Add(MakeLocal(_syntax.Declaration, declarator, LocalDeclarationKind.UsingVariable));
                }

                return locals.ToImmutable();
            }

            return ImmutableArray<LocalSymbol>.Empty;
        }

        protected override ExpressionSyntax TargetExpressionSyntax
        {
            get
            {
                return _syntax.Expression;
            }
        }

        internal override BoundStatement BindUsingStatementParts(DiagnosticBag diagnostics, Binder originalBinder)
        {
            ExpressionSyntax expressionSyntax = TargetExpressionSyntax;
            VariableDeclarationSyntax declarationSyntax = _syntax.Declaration;

            Debug.Assert((expressionSyntax == null) ^ (declarationSyntax == null)); // Can't have both or neither.

            bool hasErrors = false;
            BoundMultipleLocalDeclarations declarationsOpt = null;
            BoundExpression expressionOpt = null;
            Conversion iDisposableConversion = Conversion.NoConversion;
            TypeSymbol iDisposable = this.Compilation.GetSpecialType(SpecialType.System_IDisposable); // no need for diagnostics, so use the Compilation version
            Debug.Assert((object)iDisposable != null);

            if (expressionSyntax != null)
            {
                expressionOpt = this.BindTargetExpression(diagnostics);

                HashSet<DiagnosticInfo> useSiteDiagnostics = null;
                iDisposableConversion = this.Conversions.ClassifyImplicitConversionFromExpression(expressionOpt, iDisposable, ref useSiteDiagnostics);
                diagnostics.Add(expressionSyntax, useSiteDiagnostics);

                if (!iDisposableConversion.IsImplicit)
                {
                    TypeSymbol expressionType = expressionOpt.Type;
                    if ((object)expressionType == null || !expressionType.IsErrorType())
                    {
                        Error(diagnostics, ErrorCode.ERR_NoConvToIDisp, expressionSyntax, expressionOpt.Display);
                    }
                    hasErrors = true;
                }
            }
            else
            {
                ImmutableArray<BoundLocalDeclaration> declarations;
                BindForOrUsingOrFixedDeclarations(declarationSyntax, LocalDeclarationKind.UsingVariable, diagnostics, out declarations);

                Debug.Assert(!declarations.IsEmpty);

                declarationsOpt = new BoundMultipleLocalDeclarations(declarationSyntax, declarations);

                TypeSymbol declType = declarations[0].DeclaredType.Type;

                if (declType.IsDynamic())
                {
                    iDisposableConversion = Conversion.ImplicitDynamic;
                }
                else
                {
                    HashSet<DiagnosticInfo> useSiteDiagnostics = null;
                    iDisposableConversion = Conversions.ClassifyImplicitConversion(declType, iDisposable, ref useSiteDiagnostics);
                    diagnostics.Add(declarationSyntax, useSiteDiagnostics);

                    if (!iDisposableConversion.IsImplicit)
                    {
                        if (!declType.IsErrorType())
                        {
                            Error(diagnostics, ErrorCode.ERR_NoConvToIDisp, declarationSyntax, declType);
                        }

                        hasErrors = true;
                    }
                }
            }

            BoundStatement boundBody = originalBinder.BindPossibleEmbeddedStatement(_syntax.Statement, diagnostics);

            return new BoundUsingStatement(
                _syntax,
                this.Locals,
                declarationsOpt,
                expressionOpt,
                iDisposableConversion,
                boundBody,
                hasErrors);
        }
    }
}
