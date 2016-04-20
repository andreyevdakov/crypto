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
//            CreateSignatureFile("", true, true);

            NewSign();
//            ImportBLOB();

            Console.ReadKey();
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
//            string s = "MIICXQIBAAKBgQDcF8RUfEWAPOv1blmrVB1mdapZktnwY6MXFCqBuZIe7WMKDTuPfDXHSc5t+DKivSfizirZ6AbLkU6bvt8x4buePoQno9VEcQcmSTxYs3nBfFzSTLhK9NpV08XAaZo11CIWW/et82kfhAMDHetCMPCCAjY/3EAKSeQuabOMl7Tg0wIDAQABAoGBAJsJLuZoh7i2sWw4qHeUkAU9u5rPZC/+r8KxFOQ+qRyaEdrhyWPgli1k40H5xQl3/2G34t2OoULCf8IcKTMFFNgsIiykmjZX5xpwD+P8JOw5LNd0yvfh8ZPFLZRIG/xZLaAVY6zrKPsDy//iVBFnW3hMmRu6YF7iso97fhpGiP2hAkEA82neb9Z7YLRTERP8sMeOr9455m02qtETZ3hESAkGN678ejdvrTpoceqrvOuvRddJy4P2yqLUOrnc12kJkqAHIwJBAOd5Mgk1VFyaQhHMfspmg4bx1bSHqetTMmdqUWrNgtQ1QKvbdUBdsbQ8HEZlzbDfj6m/pG5QlZrWqS0dA4BkMpECQCdt5tJG9AVeMHZ7vlsEeGCUptxkpI5W/8Wq/aSNkaxDdDJ3+GcfJvwM/3TC2Ml/bjzBS6DXb3lz0goywZI2yfECQQC3mQu0/hXR9ZDeKUOQKsu8Z2lIbiq6uxzJpiy5+BQDWdHX/pP739UpzlvnAqyp1ElRLO6xiT2AuS8q106FsfPhAkBX0P3fUt0yxGqGNz9aZ1r5uWrX7mmRW4NgnmlZq6E6wjNcBmzHmb/JXotukiYwVDMaU94k79IylyafvERrtKPw";
//            RSACryptoServiceProvider csp = CreateRsaProviderFromPrivateKey(s);

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


        private static RSACryptoServiceProvider CreateRsaProviderFromPrivateKey(string privateKey)
        {
            var privateKeyBits = System.Convert.FromBase64String(privateKey);

            var RSA = new RSACryptoServiceProvider();
            var RSAparams = new RSAParameters();

            using (BinaryReader binr = new BinaryReader(new MemoryStream(privateKeyBits)))
            {
                byte bt = 0;
                ushort twobytes = 0;
                twobytes = binr.ReadUInt16();
                if (twobytes == 0x8130)
                    binr.ReadByte();
                else if (twobytes == 0x8230)
                    binr.ReadInt16();
                else
                    throw new Exception("Unexpected value read binr.ReadUInt16()");

                twobytes = binr.ReadUInt16();
                if (twobytes != 0x0102)
                    throw new Exception("Unexpected version");

                bt = binr.ReadByte();
                if (bt != 0x00)
                    throw new Exception("Unexpected value read binr.ReadByte()");

                RSAparams.Modulus = binr.ReadBytes(GetIntegerSize(binr));
                RSAparams.Exponent = binr.ReadBytes(GetIntegerSize(binr));
                RSAparams.D = binr.ReadBytes(GetIntegerSize(binr));
                RSAparams.P = binr.ReadBytes(GetIntegerSize(binr));
                RSAparams.Q = binr.ReadBytes(GetIntegerSize(binr));
                RSAparams.DP = binr.ReadBytes(GetIntegerSize(binr));
                RSAparams.DQ = binr.ReadBytes(GetIntegerSize(binr));
                RSAparams.InverseQ = binr.ReadBytes(GetIntegerSize(binr));
            }

            RSA.ImportParameters(RSAparams);
            return RSA;
        }

        private static int GetIntegerSize(BinaryReader binr)
        {
            byte bt = 0;
            byte lowbyte = 0x00;
            byte highbyte = 0x00;
            int count = 0;
            bt = binr.ReadByte();
            if (bt != 0x02)
                return 0;
            bt = binr.ReadByte();

            if (bt == 0x81)
                count = binr.ReadByte();
            else
                if (bt == 0x82)
                {
                    highbyte = binr.ReadByte();
                    lowbyte = binr.ReadByte();
                    byte[] modint = { lowbyte, highbyte, 0x00, 0x00 };
                    count = BitConverter.ToInt32(modint, 0);
                }
                else
                {
                    count = bt;
                }

            while (binr.ReadByte() == 0x00)
            {
                count -= 1;
            }
            binr.BaseStream.Seek(-1, SeekOrigin.Current);
            return count;
        }


    }
}
