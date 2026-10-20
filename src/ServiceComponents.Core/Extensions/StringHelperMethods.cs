using System.Security.Cryptography;

namespace ServiceComponents.Core.Extensions
{
    public static class StringHelper
    {
        public static string AddRandomPostfix(this string str, char separator = '-', int length = 10, string characterSet = RandomCharacterSet.Alphanumeric)
        {
            return $"{str}{separator}{Random(length, characterSet)}";
        }

        public static string AddRandomPrefix(this string str, char separator = '-', int length = 10, string characterSet = RandomCharacterSet.Alphanumeric)
        {
            return $"{Random(length, characterSet)}{separator}{str}";
        }

        /// <summary>
        /// Cryptographically random string of <paramref name="length"/> characters taken uniformly from <paramref name="characterSet"/>.
        /// </summary>
        public static string Random(int length, string characterSet = RandomCharacterSet.Alphanumeric)
        {
            return RandomNumberGenerator.GetString(characterSet, length);
        }
    }

    public class RandomCharacterSet
    {
        public const string Alphanumeric = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
        public const string AlphanumericAndSpecial = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890 !\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";
        public const string Letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        public const string Numbers = "1234567890";
        public const string Special = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~ ";
    }
}
