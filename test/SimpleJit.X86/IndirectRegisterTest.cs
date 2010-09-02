//
// IndirectRegisterTest.cs
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
using System.IO;

using NUnit.Framework;
using NUnit.Framework.Extensions;


namespace SimpleJit.X86 {

[TestFixture]
public class IndirectRegisterTest : BuiltinRegisters {

	void TestEncode (ModRM a, Register b, params byte[] expected) {
		MemoryStream ms = new MemoryStream ();
		a.EncodeModRm (ms, b);
		Assert.AreEqual (expected.Length, ms.Length, string.Format ("stream length {0},{1}", a, b));
		ms.Position = 0;
		for (int i = 0; i < expected.Length; ++i)
			Assert.AreEqual (expected [i], (byte)ms.ReadByte (), string.Format ("encoded index [{0}] {1},{2}", i, a, b));
	}

	[Test]
	public void IndirectRegNoDisplacement () {
		TestEncode (!EAX, EAX, 0x00);
		TestEncode (!ECX, EAX, 0x01);
		TestEncode (!EDX, EAX, 0x02);
		TestEncode (!EBX, EAX, 0x03);
		TestEncode (!ESP, EAX, 0x04, 0x24); //Must use SIB byte
		TestEncode (!EBP, EAX, 0x45, 0x00); //Must be encoded as [EBP + 0]
		TestEncode (!ESI, EAX, 0x06);
		TestEncode (!EDI, EAX, 0x07);

		TestEncode (!ECX, ESI, 0x31);
	}

	[Test]
	public void IndirectRegSmallDisplacement () {
		TestEncode (EAX + 1,   EAX, 0x40, 0x01);
		TestEncode (ECX + 2,   EAX, 0x41, 0x02);
		TestEncode (EDX + 10,  EAX, 0x42, 0x0A);
		TestEncode (EBX + 100, EAX, 0x43, 0x64);
		TestEncode (ESP + 127, EAX, 0x44, 0x24, 0x7F); 
		TestEncode (EBP - 10,  EAX, 0x45, 0xF6);
		TestEncode (ESI - 128, EAX, 0x46, 0x80);
		TestEncode (EDI - 66,  EAX, 0x47, 0xBE);

		TestEncode (EDX + 1,   EBX, 0x5A, 0x01);
	}

	[Test]
	public void IndirectRegLargeDisplacement () {
		TestEncode (EAX + 1000, EAX, 0x80, 0xE8, 0x03, 0x00, 0x00);
		TestEncode (ECX - 2000, EAX, 0x81, 0x30, 0xF8, 0xFF, 0xFF);
		TestEncode (ESP + 5000, EAX, 0x84, 0x24, 0x88, 0x13, 0x00, 0x00); 
	}

	[Test]
	public void IndirectRegReg () {
		TestEncode (EAX + EAX, EAX, 0x04, 0x00);
		TestEncode (EBP + EAX, EAX, 0x44, 0x05, 0x00);
		TestEncode (ESP + EAX, EAX, 0x04, 0x04);
		TestEncode (EBX + ECX, EDX, 0x14, 0x0B);

		TestEncode (EAX + EBP, EAX, 0x04, 0x28);
	}

	[Test]
	[ExpectedException (typeof (ArgumentException))]
	public void BadIndirectRegReg () {
		var x = EAX + ESP;
	}

	[Test]
	public void IndirectRegRegDisp () {
		TestEncode (EAX + EAX + 0x1A,   EAX, 0x44, 0x00, 0x1A);
		TestEncode (EAX + EAX + 0x2B1A, EAX, 0x84, 0x00, 0x1A, 0x2B, 0x00, 0x00);
	}

	[Test]
	public void IndirectRegRegWithScale () {
		TestEncode (EAX + EAX * 4, EAX, 0x04, 0x80);
		TestEncode (EBX + ECX * 2, EDX, 0x14, 0x4B);
		TestEncode (EBP + EDX * 2, EDX, 0x54, 0x55, 0x00);
	}

	[Test]
	public void IndirectRegRegDispWithScale () {
		TestEncode (EAX + EAX * 8 + 0x10, EAX, 0x44, 0xC0, 0x10);
		TestEncode (EBP + EDX * 4 + 0x20, EDX, 0x54, 0x95, 0x20);
		TestEncode (EAX + EAX * 8 + 0x1010, EAX, 0x84, 0xC0, 0x10, 0x10, 0x00, 0x00);
		TestEncode (EBP + EDX * 4 + 0x2020, EDX, 0x94, 0x95, 0x20, 0x20, 0x00, 0x00);
	}

	[Test]
	[ExpectedException (typeof (ArgumentException))]
	public void BadIndirectRegRegWithScale () {
		var x = EAX * 5;
	}
}

}