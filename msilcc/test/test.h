#define ASSERT(x, y) assert(x, y, #y)

#define FAKE_ASSERT(x, y) ({ y; printf("SKIP"); printf(#y); })

int printf(char* str);