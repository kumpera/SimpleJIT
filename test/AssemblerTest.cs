//
// AssemblerTest.cs
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

using System.IO;

using NUnit.Framework;
using NUnit.Framework.Extensions;

namespace SimpleJit {

[TestFixture]
public class AssemblerTest : BuiltinRegisters {
	MemoryStream ms;
	Assembler asm;
	
	[SetUp]
	public void SetUp () {
		ms = new MemoryStream ();
		asm = new Assembler (ms);
	}

	void Reset () {
		ms.Position = 0;
	}

	void AssertEncoding (params byte[] expected) {
		Assert.AreEqual (expected.Length, ms.Length, string.Format ("stream length"));
		ms.Position = 0;
		for (int i = 0; i < expected.Length; ++i)
			Assert.AreEqual (expected [i], (byte)ms.ReadByte (), string.Format ("encoded index [{0}]", i));
	}

	[Test]
	public void PushEncoding () {
		asm.Push (EAX);
		AssertEncoding (0x50);
		Reset ();

		asm.Push (EBP);
		AssertEncoding (0x55);
		Reset ();

		asm.Push (EAX + 10);
		AssertEncoding (0xFF, 0x70, 0x0A);
	}

	[Test]
	public void MovEncoding1 () {
		asm.Mov (EAX, EBX);
		AssertEncoding (0x8B, 0xC3);
		Reset ();

		asm.Mov (EAX, !EBX);
		AssertEncoding (0x8B, 0x03);
		Reset ();
	}

	[Test]
	public void MovEncoding2 () {
		asm.Mov (!EAX, EBX);
		AssertEncoding (0x89, 0x18);
		Reset ();
	}

}

}
