﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Emit;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    /// <summary>
    /// Base class for parameters can be referred to from source code.
    /// </summary>
    /// <remarks>
    /// These parameters can potentially be targetted by an attribute specified in source code. 
    /// As an optimization we distinguish simple parameters (no attributes, no modifiers, etc.) and complex parameters.
    /// </remarks>
    internal abstract class SourceParameterSymbol : SourceParameterSymbolBase
    {
        protected SymbolCompletionState state;
        protected readonly TypeSymbol parameterType;
        private readonly string _name;
        private readonly ImmutableArray<Location> _locations;
        private readonly RefKind _refKind;

        public static SourceParameterSymbol Create(
            Binder context,
            Symbol owner,
            TypeSymbol parameterType,
            ParameterSyntax syntax,
            RefKind refKind,
            SyntaxToken identifier,
            int ordinal,
            bool isParams,
            bool isExtensionMethodThis,
            bool addRefReadOnlyModifier,
            DiagnosticBag declarationDiagnostics)
        {
            var name = identifier.ValueText;
            var locations = ImmutableArray.Create<Location>(new SourceLocation(identifier));

            if (isParams)
            {
                // touch the constructor in order to generate proper use-site diagnostics
                Binder.ReportUseSiteDiagnosticForSynthesizedAttribute(context.Compilation,
                    WellKnownMember.System_ParamArrayAttribute__ctor,
                    declarationDiagnostics,
                    identifier.Parent.GetLocation());
            }

            if (addRefReadOnlyModifier && refKind == RefKind.In)
            {
                var modifierType = context.GetWellKnownType(WellKnownType.System_Runtime_InteropServices_InAttribute, declarationDiagnostics, syntax);

                return new SourceComplexParameterSymbolWithCustomModifiers(
                    owner,
                    ordinal,
                    parameterType,
                    refKind,
                    ImmutableArray<CustomModifier>.Empty,
                    ImmutableArray.Create(CSharpCustomModifier.CreateRequired(modifierType)),
                    name,
                    locations,
                    syntax.GetReference(),
                    ConstantValue.Unset,
                    isParams,
                    isExtensionMethodThis);
            }

            if (!isParams &&
                !isExtensionMethodThis &&
                (syntax.Default == null) &&
                (syntax.AttributeLists.Count == 0) &&
                !owner.IsPartialMethod())
            {
                return new SourceSimpleParameterSymbol(owner, parameterType, ordinal, refKind, name, locations);
            }

            return new SourceComplexParameterSymbol(
                owner,
                ordinal,
                parameterType,
                refKind,
                name,
                locations,
                syntax.GetReference(),
                ConstantValue.Unset,
                isParams,
                isExtensionMethodThis);
        }

        protected SourceParameterSymbol(
            Symbol owner,
            TypeSymbol parameterType,
            int ordinal,
            RefKind refKind,
            string name,
            ImmutableArray<Location> locations)
            : base(owner, ordinal)
        {
            Debug.Assert((owner.Kind == SymbolKind.Method) || (owner.Kind == SymbolKind.Property));
            this.parameterType = parameterType;
            _refKind = refKind;
            _name = name;
            _locations = locations;
        }

        internal override ParameterSymbol WithCustomModifiersAndParams(TypeSymbol newType, ImmutableArray<CustomModifier> newCustomModifiers, ImmutableArray<CustomModifier> newRefCustomModifiers, bool newIsParams)
        {
            return WithCustomModifiersAndParamsCore(newType, newCustomModifiers, newRefCustomModifiers, newIsParams);
        }

        internal SourceParameterSymbol WithCustomModifiersAndParamsCore(TypeSymbol newType, ImmutableArray<CustomModifier> newCustomModifiers, ImmutableArray<CustomModifier> newRefCustomModifiers, bool newIsParams)
        {
            newType = CustomModifierUtils.CopyTypeCustomModifiers(newType, this.Type, this.ContainingAssembly);

            if (newCustomModifiers.IsEmpty && newRefCustomModifiers.IsEmpty)
            {
                return new SourceComplexParameterSymbol(
                    this.ContainingSymbol,
                    this.Ordinal,
                    newType,
                    _refKind,
                    _name,
                    _locations,
                    this.SyntaxReference,
                    this.ExplicitDefaultConstantValue,
                    newIsParams,
                    this.IsExtensionMethodThis);
            }

            // Local functions should never have custom modifiers
            Debug.Assert(!(ContainingSymbol is LocalFunctionSymbol));

            return new SourceComplexParameterSymbolWithCustomModifiers(
                this.ContainingSymbol,
                this.Ordinal,
                newType,
                _refKind,
                newCustomModifiers,
                newRefCustomModifiers,
                _name,
                _locations,
                this.SyntaxReference,
                this.ExplicitDefaultConstantValue,
                newIsParams,
                this.IsExtensionMethodThis);
        }

        internal sealed override bool RequiresCompletion
        {
            get { return true; }
        }

        internal sealed override bool HasComplete(CompletionPart part)
        {
            return state.HasComplete(part);
        }

        internal override void ForceComplete(SourceLocation locationOpt, CancellationToken cancellationToken)
        {
            state.DefaultForceComplete(this);
        }

        /// <summary>
        /// True if the parameter is marked by <see cref="System.Runtime.InteropServices.OptionalAttribute"/>.
        /// </summary>
        internal abstract bool HasOptionalAttribute { get; }

        /// <summary>
        /// True if the parameter has default argument syntax.
        /// </summary>
        internal abstract bool HasDefaultArgumentSyntax { get; }

        internal abstract SyntaxList<AttributeListSyntax> AttributeDeclarationList { get; }

        internal abstract CustomAttributesBag<CSharpAttributeData> GetAttributesBag();

        /// <summary>
        /// Gets the attributes applied on this symbol.
        /// Returns an empty array if there are no attributes.
        /// </summary>
        public sealed override ImmutableArray<CSharpAttributeData> GetAttributes()
        {
            return this.GetAttributesBag().Attributes;
        }

        /// <summary>
        /// The declaration diagnostics for a parameter depend on the containing symbol.
        /// For instance, if the containing symbol is a method the declaration diagnostics
        /// go on the compilation, but if it is a local function it is part of the local
        /// function's declaration diagnostics.
        /// </summary>
        internal override void AddDeclarationDiagnostics(DiagnosticBag diagnostics)
            => ContainingSymbol.AddDeclarationDiagnostics(diagnostics);

        internal abstract SyntaxReference SyntaxReference { get; }

        internal abstract bool IsExtensionMethodThis { get; }

        public sealed override RefKind RefKind
        {
            get
            {
                return _refKind;
            }
        }

        public sealed override string Name
        {
            get
            {
                return _name;
            }
        }

        public sealed override ImmutableArray<Location> Locations
        {
            get
            {
                return _locations;
            }
        }

        public sealed override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences
        {
            get
            {
                return IsImplicitlyDeclared ?
                    ImmutableArray<SyntaxReference>.Empty :
                    GetDeclaringSyntaxReferenceHelper<ParameterSyntax>(_locations);
            }
        }

        public sealed override TypeSymbol Type
        {
            get
            {
                return this.parameterType;
            }
        }

        public override bool IsImplicitlyDeclared
        {
            get
            {
                // Parameters of accessors are always synthesized. (e.g., parameter of indexer accessors).
                // The non-synthesized accessors are on the property/event itself.
                MethodSymbol owningMethod = ContainingSymbol as MethodSymbol;
                return (object)owningMethod != null && owningMethod.IsAccessor();
            }
        }

        internal override void AddSynthesizedAttributes(PEModuleBuilder moduleBuilder, ref ArrayBuilder<SynthesizedAttributeData> attributes)
        {
            base.AddSynthesizedAttributes(moduleBuilder, ref attributes);

            if (this.RefKind == RefKind.In)
            {
                AddSynthesizedAttribute(ref attributes, moduleBuilder.SynthesizeIsReadOnlyAttribute(this));
            }
        }
    }
}
