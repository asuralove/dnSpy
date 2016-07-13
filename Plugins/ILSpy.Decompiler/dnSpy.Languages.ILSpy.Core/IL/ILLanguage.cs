﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnSpy.Contracts.Languages;
using dnSpy.Languages.IL;
using ICSharpCode.Decompiler.Disassembler;
using dnSpy.Decompiler.Shared;
using dnSpy.Languages.ILSpy.XmlDoc;
using dnSpy.Languages.ILSpy.Settings;
using System.Diagnostics;
using System.Text;
using dnSpy.Contracts.Text;
using dnSpy.Contracts.Languages.XmlDoc;
using dnSpy.Languages.ILSpy.Core.Text;
using dnSpy.Languages.ILSpy.Core.IL;

namespace dnSpy.Languages.ILSpy.IL {
	sealed class LanguageProvider : ILanguageProvider {
		readonly LanguageSettingsManager languageSettingsManager;

		// Keep the default ctor. It's used by dnSpy.Console.exe
		public LanguageProvider()
			: this(LanguageSettingsManager.__Instance_DONT_USE) {
		}

		public LanguageProvider(LanguageSettingsManager languageSettingsManager) {
			Debug.Assert(languageSettingsManager != null);
			if (languageSettingsManager == null)
				throw new ArgumentNullException();
			this.languageSettingsManager = languageSettingsManager;
		}

		public IEnumerable<ILanguage> Languages {
			get { yield return new ILLanguage(languageSettingsManager.ILLanguageDecompilerSettings); }
		}
	}

	/// <summary>
	/// IL language support.
	/// </summary>
	/// <remarks>
	/// Currently comes in two versions:
	/// flat IL (detectControlStructure=false) and structured IL (detectControlStructure=true).
	/// </remarks>
	sealed class ILLanguage : Language {
		readonly bool detectControlStructure;

		public override DecompilerSettingsBase Settings => langSettings;
		readonly ILLanguageDecompilerSettings langSettings;

		public ILLanguage(ILLanguageDecompilerSettings langSettings)
			: this(langSettings, true) {
		}

		public ILLanguage(ILLanguageDecompilerSettings langSettings, bool detectControlStructure) {
			this.langSettings = langSettings;
			this.detectControlStructure = detectControlStructure;
		}

		public override double OrderUI => LanguageConstants.IL_ILSPY_ORDERUI;
		public override string ContentTypeString => ContentTypesInternal.IL_ILSPY;
		public override string GenericNameUI => LanguageConstants.GENERIC_NAMEUI_IL;
		public override string UniqueNameUI => "IL";
		public override Guid GenericGuid => LanguageConstants.LANGUAGE_IL;
		public override Guid UniqueGuid => LanguageConstants.LANGUAGE_IL_ILSPY;
		public override string FileExtension => ".il";

		ReflectionDisassembler CreateReflectionDisassembler(ITextOutput output, DecompilationContext ctx, IMemberDef member) =>
			CreateReflectionDisassembler(output, ctx, member.Module);

		ReflectionDisassembler CreateReflectionDisassembler(ITextOutput output, DecompilationContext ctx, ModuleDef ownerModule) {
			var disOpts = new DisassemblerOptions(ctx.CancellationToken, ownerModule);
			if (langSettings.Settings.ShowILComments)
				disOpts.GetOpCodeDocumentation = ILLanguageHelper.GetOpCodeDocumentation;
			var sb = new StringBuilder();
			if (langSettings.Settings.ShowXmlDocumentation)
				disOpts.GetXmlDocComments = a => GetXmlDocComments(a, sb);
			disOpts.CreateInstructionBytesReader = m => InstructionBytesReader.Create(m, ctx.IsBodyModified != null && ctx.IsBodyModified(m));
			disOpts.ShowTokenAndRvaComments = langSettings.Settings.ShowTokenAndRvaComments;
			disOpts.ShowILBytes = langSettings.Settings.ShowILBytes;
			disOpts.SortMembers = langSettings.Settings.SortMembers;
			return new ReflectionDisassembler(output, detectControlStructure, disOpts);
		}

		static IEnumerable<string> GetXmlDocComments(IMemberRef mr, StringBuilder sb) {
			if (mr == null || mr.Module == null)
				yield break;
			var xmldoc = XmlDocLoader.LoadDocumentation(mr.Module);
			if (xmldoc == null)
				yield break;
			string doc = xmldoc.GetDocumentation(XmlDocKeyProvider.GetKey(mr, sb));
			if (string.IsNullOrEmpty(doc))
				yield break;

			foreach (var info in new XmlDocLine(doc)) {
				sb.Clear();
				if (info != null) {
					sb.Append(' ');
					info.Value.WriteTo(sb);
				}
				yield return sb.ToString();
			}
		}

		public override void Decompile(MethodDef method, ITextOutput output, DecompilationContext ctx) {
			var dis = CreateReflectionDisassembler(output, ctx, method);
			dis.DisassembleMethod(method);
		}

		public override void Decompile(FieldDef field, ITextOutput output, DecompilationContext ctx) {
			var dis = CreateReflectionDisassembler(output, ctx, field);
			dis.DisassembleField(field);
		}

		public override void Decompile(PropertyDef property, ITextOutput output, DecompilationContext ctx) {
			ReflectionDisassembler rd = CreateReflectionDisassembler(output, ctx, property);
			rd.DisassembleProperty(property);
			if (property.GetMethod != null) {
				output.WriteLine();
				rd.DisassembleMethod(property.GetMethod);
			}
			if (property.SetMethod != null) {
				output.WriteLine();
				rd.DisassembleMethod(property.SetMethod);
			}
			foreach (var m in property.OtherMethods) {
				output.WriteLine();
				rd.DisassembleMethod(m);
			}
		}

		public override void Decompile(EventDef ev, ITextOutput output, DecompilationContext ctx) {
			ReflectionDisassembler rd = CreateReflectionDisassembler(output, ctx, ev);
			rd.DisassembleEvent(ev);
			if (ev.AddMethod != null) {
				output.WriteLine();
				rd.DisassembleMethod(ev.AddMethod);
			}
			if (ev.RemoveMethod != null) {
				output.WriteLine();
				rd.DisassembleMethod(ev.RemoveMethod);
			}
			foreach (var m in ev.OtherMethods) {
				output.WriteLine();
				rd.DisassembleMethod(m);
			}
		}

		public override void Decompile(TypeDef type, ITextOutput output, DecompilationContext ctx) {
			var dis = CreateReflectionDisassembler(output, ctx, type);
			dis.DisassembleType(type);
		}

		public override void Decompile(AssemblyDef asm, ITextOutput output, DecompilationContext ctx) {
			output.WriteLine("// " + asm.ManifestModule.Location, BoxedTextTokenKind.Comment);
			PrintEntryPoint(asm.ManifestModule, output);
			output.WriteLine();

			ReflectionDisassembler rd = CreateReflectionDisassembler(output, ctx, asm.ManifestModule);
			rd.WriteAssemblyHeader(asm);
		}

		public override void Decompile(ModuleDef mod, ITextOutput output, DecompilationContext ctx) {
			output.WriteLine("// " + mod.Location, BoxedTextTokenKind.Comment);
			PrintEntryPoint(mod, output);
			output.WriteLine();

			ReflectionDisassembler rd = CreateReflectionDisassembler(output, ctx, mod);
			output.WriteLine();
			rd.WriteModuleHeader(mod);
		}

		protected override void TypeToString(ITextOutput output, ITypeDefOrRef t, bool includeNamespace, IHasCustomAttribute attributeProvider = null) =>
			t.WriteTo(output, includeNamespace ? ILNameSyntax.TypeName : ILNameSyntax.ShortTypeName);

		public override void WriteToolTip(IOutputColorWriter output, IMemberRef member, IHasCustomAttribute typeAttributes) {
			if (!(member is ITypeDefOrRef) && ILLanguageUtils.Write(TextColorOutputToTextOutput.Create(output), member))
				return;

			base.WriteToolTip(output, member, typeAttributes);
		}
	}
}