FILES = src/SimpleJit.X86/Assembler.cs \
		src/SimpleJit.X86/Register.cs \
		src/SimpleJit.X86/IndirectRegister.cs \
		src/SimpleJit.X86/ModRM.cs \
		src/SimpleJit.Extensions/NumericExtensions.cs	\
		src/SimpleJit.Extensions/StreamExtensions.cs	\
		src/SimpleJit.Cil/Opcode.cs \
		src/SimpleJit.Cil/MetadataTableGenerated.cs	\
		src/SimpleJit.Cil/OpcodesTableGenerated.cs \
		src/SimpleJit.Cil/Image.cs	\
		src/SimpleJit.Metadata/ClrType.cs	\
		src/SimpleJit.Metadata/MethodData.cs	\
		src/SimpleJit.Metadata/MethodBody.cs	\
		src/SimpleJit.Metadata/Signature.cs	\
		src/SimpleJit.Compiler/Compiler.cs	\
		src/SimpleJit.Compiler/FrontEnd.cs	\
		src/SimpleJit.Compiler/RegisterAllocator.cs	\
		src/SimpleJit.Compiler/ConstantPropagation.cs	\
		src/External/DataConverter.cs	\
		src/SimpleJit.Compiler/IRGenerated.cs

TEST_FILES = test/SimpleJit.X86/RegisterTest.cs \
			 test/SimpleJit.X86/IndirectRegisterTest.cs \
			 test/SimpleJit.X86/AssemblerTest.cs \

COMPILER_FILES = src/SimpleJit.Compiler/Driver.cs	\

SAMPLES = bin/simple-fun.exe

all: bin bin/SimpleJit.dll bin/SimpleJit_test.dll bin/compiler.exe basic.dll

samples : $(SAMPLES)
bin:
	mkdir bin

bin/SimpleJit.dll: $(FILES)
	mcs -debug /unsafe -target:library -out:bin/SimpleJit.dll $(FILES)

bin/SimpleJit_test.dll: bin/SimpleJit.dll $(TEST_FILES) lib/nunit.framework.dll
	mcs -debug -target:library -out:bin/SimpleJit_test.dll -r:bin/SimpleJit.dll -r:lib/nunit.framework.dll $(TEST_FILES)  

bin/compiler.exe: bin/SimpleJit.dll $(COMPILER_FILES)
	mcs -debug /unsafe -out:bin/compiler.exe /r:bin/SimpleJit.dll $(COMPILER_FILES)

src/SimpleJit.Cil/OpcodesTableGenerated.cs: src/SimpleJit.Cil.Generators/opcode-emit.rb src/SimpleJit.Cil.Generators/opcodes.xml
	ruby src/SimpleJit.Cil.Generators/opcode-emit.rb  > src/SimpleJit.Cil/OpcodesTableGenerated.cs src/SimpleJit.Cil.Generators/opcodes.xml

src/SimpleJit.Cil/MetadataTableGenerated.cs: src/SimpleJit.Cil.Generators/table-defs-emit.rb
	ruby src/SimpleJit.Cil.Generators/table-defs-emit.rb  > src/SimpleJit.Cil/MetadataTableGenerated.cs

src/SimpleJit.Compiler/IRGenerated.cs: src/SimpleJit.Compiler.Generators/ir-emit.rb
	ruby src/SimpleJit.Compiler.Generators/ir-emit.rb  > src/SimpleJit.Compiler/IRGenerated.cs

libtest.dll: libtest.cs
	mcs -debug libtest.cs -target:library -out:libtest.dll

basic.dll: basic.cs libtest.dll
	mcs -debug /unsafe basic.cs -target:library -out:basic.dll -r:libtest.dll

bin/nunit.framework.dll lib/nunit.framework.dll packages/NUnit.ConsoleRunner.3.6.1/tools/nunit3-console.exe: packages.config
	nuget install  packages.config  -OutputDirectory packages
	mkdir -p lib
	cp packages/NUnit.3.7.1/lib/net45/nunit.framework.dll lib/
	cp packages/NUnit.3.7.1/lib/net45/nunit.framework.dll bin/

compile: bin/SimpleJit.dll bin/SimpleJit_test.dll
	@echo done

run-test: bin/SimpleJit_test.dll
	 mono packages/NUnit.ConsoleRunner.3.6.1/tools/nunit3-console.exe bin/SimpleJit_test.dll

bin/%.exe: samples/%.cs
	mcs -debug /unsafe /r:bin/SimpleJit.dll -out:$@ $<

.PHONY: run-test


basic.dll_test.s basic.dll_driver.c: basic.dll bin/compiler.exe
	mono --debug bin/compiler.exe basic.dll

basic.dll_test.o: basic.dll_test.s
	clang -c basic.dll_test.s

basic.dll_driver.o: basic.dll_driver.c
	clang -c basic.dll_driver.c

basic_test: basic.dll_test.o basic.dll_driver.o
	clang basic.dll_test.o basic.dll_driver.o -o basic_test

compiler-test: basic_test
	./basic_test