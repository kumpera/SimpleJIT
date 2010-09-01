FILES = src/Register.cs \
		src/IndirectRegister.cs \
		src/ModRM.cs \
		src/StreamExtensions.cs	\
		src/Assembler.cs \

TEST_FILES = test/RegisterTest.cs \
			 test/IndirectRegisterTest.cs \
			 test/AssemblerTest.cs \

SAMPLES = bin/simple-fun.exe

all: bin bin/SimpleJit.dll bin/SimpleJit_test.dll

samples : $(SAMPLES)
bin:
	mkdir bin

bin/SimpleJit.dll: $(FILES)
	gmcs -debug /unsafe -target:library -out:bin/SimpleJit.dll $(FILES)

bin/SimpleJit_test.dll: bin/SimpleJit.dll $(TEST_FILES)
	gmcs -debug -target:library -out:bin/SimpleJit_test.dll -r:bin/SimpleJit.dll -r:nunit.framework.dll -r:nunit.framework.extensions.dll  -r:nunit.core.dll $(TEST_FILES)  


compile: bin/SimpleJit.dll bin/SimpleJit_test.dll
	@echo done

run-test: bin/SimpleJit_test.dll
	 nunit-console2 bin/SimpleJit_test.dll

bin/%.exe: samples/%.cs
	gmcs /unsafe /r:bin/SimpleJit.dll -out:$@ $<

.PHONY: run_test