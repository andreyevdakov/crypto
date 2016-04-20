/*
 * DELL PROPRIETARY INFORMATION
 *
 * This software is confidential.  Dell Inc., or one of its subsidiaries, has
 * supplied this software to you under the terms of a license agreement,
 * nondisclosure agreement or both.  You may not copy, disclose, or use this 
 * software except in accordance with those terms.
 *
 * Copyright 2015 Dell Inc.  
 * ALL RIGHTS RESERVED.
 *
 * DELL INC. MAKES NO REPRESENTATIONS OR WARRANTIES
 * ABOUT THE SUITABILITY OF THE SOFTWARE, EITHER EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
 * PARTICULAR PURPOSE, OR NON-INFRINGEMENT. DELL SHALL
 * NOT BE LIABLE FOR ANY DAMAGES SUFFERED BY LICENSEE
 * AS A RESULT OF USING, MODIFYING OR DISTRIBUTING
 * THIS SOFTWARE OR ITS DERIVATIVES.
 *
 */

using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ScriptLogic.DAConsole.Common
{
    public class Crypto
    {
        public enum CryptoSettingsVersion
        {
            NewCryptoSet,
            OldCryptoSet
        }

        #region Properties

        public static string Provider
        {
            get
            {
                if (Environment.OSVersion.Version.Major == 5 && Environment.OSVersion.Version.Minor == 1)
                    return MS_ENH_RSA_AES_PROV_OLD;

                return MS_ENH_RSA_AES_PROV_NEW;
            }
        }
        public static uint Type{get { return PROV_RSA_AES; }}
        public static uint HashAlgorithm{get { return CALG_SHA_512; }}
        public static uint SymmetricEncryptAlgorithm{get { return CALG_3DES; } }

        public static string OldProvider { get{return MS_DEF_PROV;}}
        public static uint OldType { get { return PROV_RSA_FULL; } }
        public static uint OldHashAlgorithm { get { return CALG_SHA; } }
        public static uint OldSymmetricEncryptAlgorithm { get { return CALG_RC4; } }




        #endregion


        #region Crypto API imports

        private const uint ALG_CLASS_HASH = (4 << 13);
        private const uint ALG_TYPE_ANY = (0);
        private const uint ALG_CLASS_DATA_ENCRYPT = (3 << 13);
        private const uint ALG_TYPE_STREAM = (4 << 9);
        private const uint ALG_TYPE_BLOCK = (3 << 9);

        private const uint ALG_SID_DES = 1;
        private const uint ALG_SID_RC4 = 1;
        private const uint ALG_SID_RC2 = 2;
        private const uint ALG_SID_MD5 = 3;
        private const uint ALG_SID_SHA = 4;
        private const uint ALG_SID_SHA_512 = 14;
        private const uint ALG_SID_3DES = 3;

        public const string MS_DEF_PROV = "Microsoft Base Cryptographic Provider v1.0";
        public const string MS_ENHANCED_PROV = "Microsoft Enhanced Cryptographic Provider v1.0";
        public const string MS_ENH_RSA_AES_PROV_OLD = "Microsoft Enhanced RSA and AES Cryptographic Provider (Prototype)";
        public const string MS_ENH_RSA_AES_PROV_NEW = "Microsoft Enhanced RSA and AES Cryptographic Provider";

        public const uint PROV_RSA_FULL = 1;
        public const uint PROV_RSA_AES = 24;
        public const uint CRYPT_VERIFYCONTEXT = 0xf0000000;
        public const uint CRYPT_EXPORTABLE = 0x00000001;
        public const uint AT_SIGNATURE = 2;
        public const int PUBLICKEYBLOB = 6;
        public const int PRIVATEKEYBLOB = 7;

        public const uint HP_HASHVAL = 0x0002;
        public const uint HP_HASHSIZE = 0x0004;

        public enum KeyType
        {
            Public = 6,
            Private = 7
        }

        public static readonly uint CALG_MD5 =
            (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_MD5);
        public static readonly uint CALG_DES =
            (ALG_CLASS_DATA_ENCRYPT | ALG_TYPE_BLOCK | ALG_SID_DES);
        public static readonly uint CALG_RC2 =
            (ALG_CLASS_DATA_ENCRYPT | ALG_TYPE_BLOCK | ALG_SID_RC2);
        public static readonly uint CALG_RC4 =
            (ALG_CLASS_DATA_ENCRYPT | ALG_TYPE_STREAM | ALG_SID_RC4);
        public static readonly uint CALG_SHA =
            (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_SHA);
        public static readonly uint CALG_SHA_512 =
            (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_SHA_512);
        public static readonly uint CALG_3DES =
            (ALG_CLASS_DATA_ENCRYPT | ALG_TYPE_BLOCK | ALG_SID_3DES);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CryptAcquireContext(
            ref IntPtr phProv, string pszContainer, string pszProvider,
            uint dwProvType, uint dwFlags);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CryptGetKeyParam(
            IntPtr hKey, int dwParam, ref byte[] pbdata, int dwDataLen, uint dwFlags);
        
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CryptSetKeyParam(
            IntPtr hKey, int dwParam, byte[] pbdata, uint dwFlags);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CryptImportKey(
            IntPtr hProv, byte[] pbData, int dwDataLen,
            IntPtr hPubKey, uint dwFlags, ref IntPtr phKey);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CryptReleaseContext(
            IntPtr hProv, uint dwFlags);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CryptDeriveKey(
            IntPtr hProv, uint Algid, IntPtr hBaseData,
            uint dwFlags, ref IntPtr phKey);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CryptCreateHash(
            IntPtr hProv, uint Algid, IntPtr hKey,
            uint dwFlags, ref IntPtr phHash);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CryptGetHashParam(
            IntPtr hHash, uint dwParam, byte[] pbData,
            ref uint pdwDataLen, uint dwFlags);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CryptHashData(
            IntPtr hHash, byte[] pbData,
            int dwDataLen, uint dwFlags);

        [DllImport("Advapi32.dll", SetLastError = true)]
        public static extern Boolean CryptSignHash(
            IntPtr hHash, uint dwKeySpec, string sDescription,
            uint dwFlags, byte[] pbSignature, ref int pdwSigLen);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CryptEncrypt(
            IntPtr hKey, IntPtr hHash, bool Final, uint dwFlags,
            byte[] pbData, ref uint pdwDataLen, uint dwBufLen);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CryptDecrypt(
            IntPtr hKey, IntPtr hHash, bool Final, uint dwFlags,
            byte[] pbData, ref uint pdwDataLen);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CryptDestroyHash(IntPtr hHash);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CryptDestroyKey(IntPtr hKey);

        [DllImport(@"advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool CryptExportKey(IntPtr hKey, IntPtr hExpKey,
            KeyType dwBlobType, uint dwFlags, [In, Out] byte[] pbData, ref uint dwDataLen);

        [DllImport(@"advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool CryptGenKey(IntPtr hProv, uint Algid, uint dwFlags, ref IntPtr phKey);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool CryptVerifySignature(IntPtr hHash, [In] byte[] signature, uint signatureLen,
            IntPtr pubKey, IntPtr description, uint flags);

        #endregion

        #region Error reporting imports

        [DllImport("kernel32.dll")]
        public static extern uint GetLastError();

        #endregion

        //static public byte[] EncipherSHA256toByte(string val)
        //{
        //    HashAlgorithm hashAlgoritm = SHA256.Create();
        //    return hashAlgoritm.ComputeHash(Encoding.UTF8.GetBytes(val));
        //}

        //static public string EncipherHSA256toString(string val)
        //{
        //    byte[] buff = EncipherSHA256toByte(val);
        //    return Convert.ToBase64String(buff);
        //}
    }
}
