#region License notice

/*
  This file is part of the Ceres project at https://github.com/dje-dev/ceres.
  Copyright (C) 2020- by David Elliott and the Ceres Authors.

  Ceres is free software under the terms of the GNU General Public License v3.0.
  You should have received a copy of the GNU General Public License
  along with Ceres. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

#region Using directives


#endregion

namespace Ceres.Base.Misc
{
  /// <summary>
  /// Static helper methods for working with strings.
  /// </summary>
  public static class StringUtils
  {
    /// <summary>
    /// Adjust a string's length to be exactly specified value,
    /// truncating or padding on the right as needed.
    /// </summary>
    /// <param name="s"></param>
    /// <param name="len"></param>
    /// <returns></returns>
    public static string Sized(string s, int len)
    {
      if (s.Length > len) return s.Substring(0, len);

      while (s.Length < len) s = s + " ";
      return s;
    }
  }
}
