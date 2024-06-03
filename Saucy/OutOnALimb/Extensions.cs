using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saucy.OutOnALimb;
public static class Extensions
{
    public static string RemoveSpaces(this string s) => s.Replace(" ", "");
}
