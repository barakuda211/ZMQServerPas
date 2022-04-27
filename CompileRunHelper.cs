using System.IO;
using System;
using PascalABCCompiler.Errors;

namespace ZMQServerPas
{
    public class Helper {
        public static string CreateTempPas(string code) {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = System.IO.Path.GetDirectoryName(exe);
            var myfilePath = exeDir + "\\temp\\"+Guid.NewGuid().ToString()+".pas";
            //myfilename = Path.ChangeExtension(myfilename, "pas");

            using (StreamWriter sw = new StreamWriter(myfilePath))
            {
                sw.Write(code);
            }
            return myfilePath;
        }
        public static string EnhanceErrorMsg(Object err0) {
            var err = err0 as LocatedError;
            var msg = err.ToString();
            string res = "";
            var ind1 = msg.IndexOf('(');
            var ind2 = msg.IndexOf(')');
            var pos = "";
            if (ind1 > -1 && ind2 > -1) {
                pos = msg.Substring(ind1, ind2 + 1 - ind1); //нет функционала безопасных срезов
            }
            if (ind2 > -1 && ind2 < msg.Length) {
                ind2 = msg.IndexOf(':', ind2);
                if (ind2 > -1 && ind2 < msg.Length - 1)
                    ind2 = msg.IndexOf(':', ind2 + 1);
                res = msg.Substring(ind2 + 1).Trim(' ');
                if (pos != "")
                    res = pos + ": " + res;
                if (err0 is SemanticError) {
                    SemanticError semErr = err0 as SemanticError;
                    pos = '(' + semErr.Location.begin_line_num + "," + semErr.Location.begin_column_num + ')';
                    res = pos + ": " + res;
                }
            }
            return res;
        }
    }
}
