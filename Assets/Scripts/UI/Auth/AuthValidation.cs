using System.Text.RegularExpressions;

namespace ProjectS.UI
{
    /// <summary>로그인/가입 입력의 클라이언트 사전 검증(Firebase 왕복 전에 즉시 피드백).</summary>
    internal static class AuthValidation
    {
        // 엄격한 RFC 검증이 아니라 실용 수준(로컬@도메인.tld). 최종 판정은 Firebase가 한다.
        private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        /// <summary>이메일 형식이 유효한지.</summary>
        public static bool IsValidEmail(string email)
            => !string.IsNullOrEmpty(email) && EmailRegex.IsMatch(email);

        /// <summary>비밀번호 길이가 Firebase 최소치(6자) 이상인지.</summary>
        public static bool IsValidPassword(string password)
            => !string.IsNullOrEmpty(password) && password.Length >= 6;
    }
}
