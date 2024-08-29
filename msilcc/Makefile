CFLAGS=-std=c11 -g -fno-common

SRCS=$(wildcard *.cs)
OBJS=$(SRCS:.c=.o)

TEST_SRCS=$(wildcard test/*.c)
TESTS=$(TEST_SRCS:.c=.exe)

msilcc: $(SRCS)
	dotnet publish -c Debug -r linux-x64 -p:PublishReadyToRun=true

test/%.exe: msilcc test/%.c
	$(CC) -o- -E -P -C test/$*.c | dotnet exec bin/Debug/net9.0/linux-x64/publish/msilcc.dll -o test/$*.exe -

test: $(TESTS)
	for i in $^; do echo $$i; dotnet exec ./$$i || exit 1; echo; done
	test/driver.sh

clean:
	rm -rf tmp* $(TESTS)
	find * -type f '(' -name '*~' -o -name '*.o' ')' -exec rm {} ';'

isolate:
	rm test/isolate.exe || true
	$(CC) -o- -E -P -C test/isolate.c | ./msilcc -o test/isolate.exe -
	dotnet exec test/isolate.exe

.PHONY: test clean
