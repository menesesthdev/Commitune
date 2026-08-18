namespace Commitune.Api.Bot;

/// <summary>
/// Folds accented letters onto their ASCII counterpart. Portuguese is full of them and every
/// name Commitune generates — repositories, file paths, tags — has to survive as ASCII, so
/// dropping the accent always beats dropping the letter.
/// </summary>
internal static class Accents
{
    public static char? Fold(char character) => char.ToLowerInvariant(character) switch
    {
        'á' or 'à' or 'â' or 'ã' or 'ä' => 'a',
        'é' or 'è' or 'ê' or 'ë' => 'e',
        'í' or 'ì' or 'î' or 'ï' => 'i',
        'ó' or 'ò' or 'ô' or 'õ' or 'ö' => 'o',
        'ú' or 'ù' or 'û' or 'ü' => 'u',
        'ç' => 'c',
        'ñ' => 'n',
        _ => null,
    };
}
