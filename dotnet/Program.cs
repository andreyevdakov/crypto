using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using System.IO;
using ScriptLogic.DAConsole.Common;

namespace CryptoDA
{
    class Program
    {
        static void Main(string[] args)
        {
            VerifySigning();

//            CreateSignatureFile("", true, true);

//            NewSign();
//            ImportBLOB();

            Console.ReadKey();
        }

        private static void VerifySigning()
        {
            string publicKey = "<RSAKeyValue><Modulus>nufyKCsoNhhoa/gYkafiRNYOvCONnq5C2Zk9CpNUJehWXyi7V5FyiyHNn6kzal6XJGd29nFXCWaDMEd/zgrozL+NuWPuhBEfbVu7h/ugBGDRy6UZlbQVL2lvpWLqdyE/bEVSPdUduu5c3DoMqlPVlR8GHJ5LBJQIJRm+FaPKly8=</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
            RSACryptoServiceProvider cspV = new RSACryptoServiceProvider();
            cspV.FromXmlString(publicKey);

            string line;
            System.IO.StreamReader file = new System.IO.StreamReader("slSigs.ini");
            while ((line = file.ReadLine()) != null)
            {
                if (line.Contains('='))
                {
                    string fileName = line.Substring(0, line.IndexOf('='));
                    string signStr = line.Substring(line.IndexOf('=') + 1);
                    Console.Write(fileName + " file verification: ");

                    byte[] data = ReadAllBytes(fileName);
                    SHA512Managed sha512 = new SHA512Managed();
                    byte[] hash = sha512.ComputeHash(data);
                    byte[] sign = Convert.FromBase64String(signStr);
                    bool check = cspV.VerifyHash(hash, CryptoConfig.MapNameToOID("SHA512"), sign);

                    if (check)
                        Console.WriteLine("true");
                    else
                        Console.WriteLine("false");
                }

            }
            file.Close();
        }

        private static void ImportBLOB()
        {
            RSACryptoServiceProvider csp = new RSACryptoServiceProvider();
            byte[] key = GeyKeyData("slsrvmgr.ske");
            csp.ImportCspBlob(key);
            RSAParameters RSAParams = csp.ExportParameters(true);
            string xmlPrivate = csp.ToXmlString(true);
            string xmlPublic = csp.ToXmlString(false);

        }

        private static void NewSign()
        {
            RSACryptoServiceProvider csp = new RSACryptoServiceProvider();
            byte[] key = GeyKeyData("slsrvmgr.ske");
            csp.ImportCspBlob(key);

            SHA512Managed sha512 = new SHA512Managed();

            byte[] data = ReadAllBytes("SLBoost.ini");

            byte[] hash = sha512.ComputeHash(data);

            // Sign the hash
            byte[] res = csp.SignHash(hash, CryptoConfig.MapNameToOID("SHA512"));
            string st = Convert.ToBase64String(res);


            // Verify the hash
            RSACryptoServiceProvider cspV = new RSACryptoServiceProvider();
            cspV.FromXmlString(csp.ToXmlString(false));

            bool check = cspV.VerifyHash(hash, CryptoConfig.MapNameToOID("SHA512"), res);

        }

        private static void CreateSignatureFile(string signFilePath, bool isUbmReplication, bool isCbmReplication)
        {
            var provider = IntPtr.Zero;
            var keyFilePath = "slsrvmgr.ske";

            byte[] key = GeyKeyData(keyFilePath);

            try
            {
                if (!Crypto.CryptAcquireContext(ref provider, null, Crypto.Provider, Crypto.Type,
                                                Crypto.CRYPT_VERIFYCONTEXT))
                    throw new Exception("CryptAcquireContext");

                var sigKey = IntPtr.Zero;

                if (!Crypto.CryptImportKey(provider, key, key.Length, IntPtr.Zero, Crypto.CRYPT_EXPORTABLE, ref sigKey))
                    throw new Exception("CryptImportKey");

                FillSignFile(provider, signFilePath, isUbmReplication, isCbmReplication);
            }
            finally
            {
                Crypto.CryptReleaseContext(provider, 0);
            }
        }

        private static void FillSignFile(IntPtr provider, string signaturesFilePath, bool isUbmReplication, bool isCbmReplication)
        {
            var signFiles = new List<FileInfo>();
            if (isUbmReplication)
            {
                var ubmFilesToSign = new[]
                                     {
                                         "slboost.ini"
                                     }.Select(
                                                 ubmFile => new FileInfo(ubmFile));

                signFiles.AddRange(ubmFilesToSign);
            }

            foreach (var file in signFiles)
            {
                if (!file.Exists)
                    continue;

                IntPtr refHash = IntPtr.Zero;

                if (!Crypto.CryptCreateHash(provider, Crypto.HashAlgorithm, IntPtr.Zero, 0, ref refHash))
                    throw new Exception("CryptCreateHash");

                byte[] buf = ReadAllBytes(file.FullName);
                if (buf.Length > 0)
                {
                    if (!Crypto.CryptHashData(refHash, buf, buf.Length, 0))
                        throw new Exception("CryptHashData");
                }

                byte[] lpSignature = null;
                int dwSignatureSize = 0;
                if (!Crypto.CryptSignHash(refHash, Crypto.AT_SIGNATURE, null, 0, lpSignature, ref dwSignatureSize))
                    throw new Exception("CryptSignHash");

                lpSignature = new byte[dwSignatureSize];

                if (!Crypto.CryptSignHash(refHash, Crypto.AT_SIGNATURE, null, 0, lpSignature, ref dwSignatureSize))
                    throw new Exception("CryptSignHash");

                string encodedSignature = Convert.ToBase64String(lpSignature);

            }
        }

        private static byte[] GeyKeyData(string keyFilePath)
        {
            using (var keyFile = File.Open(keyFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var length = (int)keyFile.Length;
                var bytes = new byte[length];
                var byteRead = keyFile.Read(bytes, 0, length);
                var encodeKey = Encoding.ASCII.GetString(bytes, 0, byteRead);
                return Convert.FromBase64String(encodeKey);
            }
        }

        public static byte[] ReadAllBytes(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var buffer = new byte[8192];

                using (var tmpStream = new MemoryStream())
                {
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        tmpStream.Write(buffer, 0, bytesRead);
                    }

                    return tmpStream.ToArray();
                }
            }
        }

    }
}
