//
// Driver.cs
//
// Author:
//   Rodrigo Kumpera  <kumpera@gmail.com>
//
//
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System;
using System.Collections.Generic;
using System.IO;
using SimpleJit.CIL;
using SimpleJit.Metadata;

namespace SimpleJit.Compiler {
public class Driver {
	static void Main (string[] args) {
		Console.WriteLine ("compiling {0}", args[0]);
		Image img = new Image (File.ReadAllBytes (args [0]));

		Console.WriteLine ("we have {0} methods", img.MethodDefTable.Count);
		using (StreamWriter asm = new StreamWriter (args[0] + "_test.s"), c_test = new StreamWriter (args[0] + "_driver.c")) {
			EmitAsmHeader (asm);
			var ml = new List<MethodData>  ();
			for (int i = 0; i < img.MethodDefTable.Count; ++i) {
				var method = img.LoadMethodDef (i);
				// if (method.Name != "test_1_load_bool")
				if (method.Name == "Main")
					continue;

				Compiler compiler = new Compiler (method);
				compiler.Run (asm);
				if (method.Name.StartsWith ("test_"))
					ml.Add (method);
				asm.Flush ();
			}
			EmitCTestCode (c_test, ml);
			
		}
	}

	static void EmitAsmHeader (StreamWriter asm)
	{
		asm.WriteLine (".section	__TEXT,__text,regular,pure_instructions");
	}

	static void GenTestCode (StreamWriter c_test, string methodName, string expected, string args) {
		c_test.WriteLine ($"\tres = {methodName} ({args});");
		c_test.WriteLine ($"\tif (res != {expected}) {{");
		c_test.WriteLine ($"\t\tprintf (\"test {methodName} returned %d expected {expected}\\n\", res);");
		c_test.WriteLine ($"\t\tsuite_res = 1;");
		c_test.WriteLine ("\t}");
	}

	static void EmitCTestCode (StreamWriter c_test, List<MethodData> ml)
	{
		c_test.WriteLine ("#include <stdio.h>");

		foreach (var m in ml) {
			string argList;
			if (m.Signature.ParamCount == 0) {
				argList = "void";
			} else {
				argList = "";
				for (int i = 0; i < m.Signature.ParamCount; ++i) {
					if (i > 0)
						argList += ", ";
					argList += $"int arg_{i}";
				}
			}

			c_test.WriteLine ($"extern int {m.Name}({argList});");
		}

		c_test.WriteLine ("\nint main (int argc, char *argv[]) {");
		c_test.WriteLine ("\tint suite_res = 0;");
		c_test.WriteLine ("\tint res;");

		foreach (var m in ml) {
			if (m.Signature.ParamCount == 0) {
				GenTestCode (c_test, m.Name, m.Name.Split ('_')[1], "");
			} else {
				var sp = m.Name.Split ('_');
				string argList = "";
				for (int i = 0; i < m.Signature.ParamCount; ++i) {
					if (i > 0)
						argList += ", ";
					argList += sp [i + 2];
				};

				GenTestCode (c_test, m.Name, sp[1], argList);
			}
		}

		c_test.WriteLine ("\treturn suite_res;");
		c_test.WriteLine ("}");

	}
}
}