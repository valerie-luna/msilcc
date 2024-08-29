namespace Msilcc.Parser;

public enum BinaryKind
{
    Undefined = 0,
    Add = 1 | Numeric | Symmetric,
    Sub = 2 | Numeric,
    Mul = 3 | Numeric | Symmetric,
    Div = 4 | Numeric,
    Equal = 5 | Comparision | Symmetric,
    NotEqual = 6 | Comparision | Symmetric,
    LessThan = 7 | Comparision,
    LessThanOrEqual = 8 | Comparision,
    Numeric = 0b10_0000,
    Comparision = 0b01_0000,
    Symmetric = 0b1_00_0000
}
