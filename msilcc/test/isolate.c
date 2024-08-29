#include "test.h"

int main() {
  union { int a; char b[6]; } x;
  return 0;
}
