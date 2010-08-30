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

using System.IO;

using NUnit.Framework;
using NUnit.Framework.Extensions;


namespace SimpleJit {

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
	public void IndirectRegNoDisplacement ()
	{
		TestEncode (!EAX, EAX, 0x00);
		TestEncode (!ECX, EAX, 0x01);
		TestEncode (!EDX, EAX, 0x02);
		TestEncode (!EBX, EAX, 0x03);
		TestEncode (!ESP, EAX, 0x04, 0x24); //Must use SIB byte
		TestEncode (!EBP, EAX, 0x45, 0x00); //Must be encoded as [EBP + 0]
		TestEncode (!ESI, EAX, 0x06);
		TestEncode (!EDI, EAX, 0x07);
	}

	[Test]
	public void IndirectRegSmallDisplacement ()
	{
	}

}

}