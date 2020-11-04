using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Arius
{
    class AriusFile
    {
        public static string GetLocalContentName(string relativeName)
        {
            //Ref https://stackoverflow.com/questions/5650909/regex-for-extracting-certain-part-of-a-string

            var match = Regex.Match(relativeName, "^(?<relativeName>.*).arius$");
            return match.Groups["relativeName"].Value;
        }
    }
}
