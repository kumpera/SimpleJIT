FILES = src/Register.cs \
		src/IndirectRegister.cs \
		src/ModRM.cs \
		src/StreamExtensions.cs	\
		src/Assembler.cs \

TEST_FILES = test/RegisterTest.cs \
			 test/IndirectRegisterTest.cs \
			 test/AssemblerTest.cs \

all: SimpleJit.dll SimpleJit_test.dll

SimpleJit.dll: $(FILES)
	gmcs -debug /unsafe -target:library -out:SimpleJit.dll $(FILES)

SimpleJit_test.dll: SimpleJit.dll $(TEST_FILES)
	gmcs -debug -target:library -out:SimpleJit_test.dll -r:SimpleJit.dll -r:nunit.framework.dll -r:nunit.framework.extensions.dll  -r:nunit.core.dll $(TEST_FILES)  


compile: SimpleJit.dll SimpleJit_test.dll
	@echo done

run-test: SimpleJit_test.dll
	 nunit-console2 SimpleJit_test.dll


.PHONY: run_test