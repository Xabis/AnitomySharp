﻿/*
 * Copyright (c) 2014-2017, Eren Okka
 * Copyright (c) 2016-2017, Paul Miller
 * Copyright (c) 2017-2018, Tyler Bratton
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnitomySharp
{

  /// <summary>
  /// A utility class to assist in number parsing.
  /// </summary>
  public class ParserNumber
  {
    public static readonly int AnimeYearMin = 1900;
    public static readonly int AnimeYearMax = 2050;
    public static readonly int EpisodeNumberMax = AnimeYearMax - 1;
    public static readonly int VolumeNumberMax = 20;
    private static readonly string regexMatchOnlyStart = @"\A(?:";
    private static readonly string regexMatchOnlyEnd = @")\z";

    private readonly Parser _parser;

    public ParserNumber(Parser parser)
    {
      _parser = parser;
    }

    /// <summary>
    /// Returns whether or not the <code>number</code> is a volume number
    /// </summary>
    public bool IsValidVolumeNumber(string number)
    {
      return StringHelper.StringToInt(number) <= VolumeNumberMax;
    }

    /// <summary>
    /// Returns whether or not the <code>number</code> is a valid episode number.
    /// </summary>
    public bool IsValidEpisodeNumber(string number)
    {
      // Eliminate non numeric portion of number, then parse as double.
      var temp = "";
      for (var i = 0; i < number.Length && char.IsDigit(number[i]); i++)
      {
        temp += number[i];
      }

      return !string.IsNullOrEmpty(temp) && double.Parse(temp) <= EpisodeNumberMax;
    }

    /// <summary>
    /// Sets the alternative episode number.
    /// </summary>
    public bool SetAlternativeEpisodeNumber(string number, Token token)
    {
      _parser.Elements.Add(new Element(Element.ElementCategory.ElementEpisodeNumberAlt, number));
      token.Category = Token.TokenCategory.Identifier;
      return true;
    }

    /// <summary>
    /// Sets the volume number.
    /// </summary>
    /// <param name="number">the number</param>
    /// <param name="token">the token which contains the volume number</param>
    /// <param name="validate">true if we should check if it's a valid number, false to disable verification</param>
    /// <returns>true if the volume number was set</returns>
    public bool SetVolumeNumber(string number, Token token, bool validate)
    {
      if (validate && !IsValidVolumeNumber(number))
      {
        return false;
      }
      else
      {
        _parser.Elements.Add(new Element(Element.ElementCategory.ElementVolumeNumber, number));
        token.Category = Token.TokenCategory.Identifier;
        return true;
      }
    }

    /// <summary>
    /// Sets the anime episode number.
    /// </summary>
    /// <param name="number">the episode number</param>
    /// <param name="token">the token which contains the volume number</param>
    /// <param name="validate">true if we should check if it's a valid episode number; false to disable validation</param>
    /// <returns>true if the episode number was set</returns>
    public bool SetEpisodeNumber(string number, Token token, bool validate)
    {
      if (validate && !IsValidEpisodeNumber(number)) return false;
      token.Category = Token.TokenCategory.Identifier;
      var category = Element.ElementCategory.ElementEpisodeNumber;

      /** Handle equivalent numbers */
      if (_parser.IsEpisodeKeywordsFound)
      {
        foreach (var element in _parser.Elements)
        {
          if (element.Category != Element.ElementCategory.ElementEpisodeNumber) continue;

          /** The larger number gets to be the alternative one */
          int comparison = StringHelper.StringToInt(number) - StringHelper.StringToInt(element.Value);
          if (comparison > 0)
          {
            category = Element.ElementCategory.ElementEpisodeNumberAlt;
          } 
          else if (comparison < 0)
          {
            element.Category = Element.ElementCategory.ElementEpisodeNumberAlt;
          }
          else
          {
            return false; /** No need to add the same number twice */
          }

          break;
        }
      }

      _parser.Elements.Add(new Element(category, number));
      return true;
    }

    /// <summary>
    /// Checks if a number follows the specified <code>token</code>
    /// </summary>
    /// <param name="category">the category to set if a number follows the <code>token</code></param>
    /// <param name="token">the token</param>
    /// <returns>true if a number follows the token; false otherwise</returns>
    public bool NumberComesAfterPrefix(Element.ElementCategory category, Token token)
    {
      var numberBegin = ParserHelper.IndexOfFirstDigit(token.Content);
      var prefix = StringHelper.SubstringWithCheck(token.Content, 0, numberBegin).ToUpperInvariant();
      if (KeywordManager.Instance.Contains(category, prefix))
      {
        var number = StringHelper.SubstringWithCheck(token.Content, numberBegin, token.Content.Length - numberBegin);

        switch (category)
        {
            case Element.ElementCategory.ElementEpisodePrefix:
              if (!MatchEpisodePatterns(number, token))
              {
                SetEpisodeNumber(number, token, false);
              }
              return true;
            case Element.ElementCategory.ElementVolumePrefix:
              if (!MatchVolumePatterns(number, token))
              {
                SetVolumeNumber(number, token, false);
              }
              return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Checks whether the number precedes the word "of"
    /// </summary>
    /// <param name="token">the token</param>
    /// <param name="currentTokenIdx">the index of the token</param>
    /// <returns>true if the token precedes the word "of"</returns>
    public bool NumberComesBeforeTotalNumber(Token token, int currentTokenIdx)
    {
      Result nextToken = Token.FindNextToken(_parser.Tokens, currentTokenIdx, Token.TokenFlag.FlagNotDelimiter);
      if (nextToken.Token != null)
      {
        if (nextToken.Token.Content.Equals("of", StringComparison.InvariantCultureIgnoreCase))
        {
          Result otherToken = Token.FindNextToken(_parser.Tokens, nextToken, Token.TokenFlag.FlagNotDelimiter);

          if (otherToken.Token != null)
          {
            if (StringHelper.IsNumericString(otherToken.Token.Content))
            {
              SetEpisodeNumber(token.Content, token, false);
              nextToken.Token.Category = Token.TokenCategory.Identifier;
              otherToken.Token.Category = Token.TokenCategory.Identifier;
              return true;
            }
          }
        }
      }

      return false;
    }

    // EPISODE MATCHERS

    /// <summary>
    /// Attempts to find an episode/season inside a <code>word</code>
    /// </summary>
    /// <param name="word">the word</param>
    /// <param name="token">the token</param>
    /// <returns>true if the word was matched to an episode/season number</returns>
    public bool MatchEpisodePatterns(string word, Token token)
    {
      if (StringHelper.IsNumericString(word)) return false;

      word = word.Trim(" -".ToCharArray());

      var numericFront = char.IsDigit(word[0]);
      var numericBack = char.IsDigit(word[word.Length - 1]);

      if (numericFront && numericBack)
      {
        // e.g. "01v2"
        if (MatchSingleEpisodePattern(word, token))
        {
          return true;
        }
        // e.g. "01-02", "03-05v2"
        else if (MatchMultiEpisodePattern(word, token))
        {
          return true;
        }
        // e.g. "07.5"
        else if (MatchFractionalEpisodePattern(word, token))
        {
          return true;
        }
      }

      if (numericBack)
      {
        // e.g. "2x01", "S01E03", "S01-02xE001-150"
        if (MatchSeasonAndEpisodePattern(word, token))
        {
          return true;
        }
        // e.g. "#01", "#02-03v2"
        else if (MatchNumberSignPattern(word, token))
        {
          return true;
        }
      }

      // e.g. "ED1", "OP4a", "OVA2"
      if (!numericFront && MatchTypeAndEpisodePattern(word, token))
      {
        return true;
      }

      // e.g. "4a", "111C"
      if (numericFront && !numericBack && MatchPartialEpisodePattern(word, token))
      {
        return true;
      }
      
      // U+8A71 is used as counter for stories, episodes of TV series, etc.
      if (numericFront && MatchJapaneseCounterPattern(word, token))
      {
        return true;
      }
      return false;
    }

    /// <summary>
    /// Match a single episode pattern. e.g. "01v2".
    /// </summary>
    /// <param name="word">the word</param>
    /// <param name="token">the token</param>
    /// <returns>true if the token matched</returns>
    public bool MatchSingleEpisodePattern(string word, Token token)
    {
      var regexPattern = regexMatchOnlyStart + @"(\d{1,3})[vV](\d)" + regexMatchOnlyEnd;
      var match = Regex.Match(word, regexPattern);

      if (match.Success)
      {
        SetEpisodeNumber(match.Groups[1].Value, token, false);
        _parser.Elements.Add(new Element(Element.ElementCategory.ElementReleaseVersion, match.Groups[2].Value));
        return true;
      }

      return false;
    }

    /// <summary>
    /// Match a multi episode pattern. e.g. "01-02", "03-05v2".
    /// </summary>
    /// <param name="word">the word</param>
    /// <param name="token">the token</param>
    /// <returns>true if the token matched</returns>
    public bool MatchMultiEpisodePattern(string word, Token token)
    {
      var regexPattern = regexMatchOnlyStart + @"(\d{1,3})(?:[vV](\d))?[-~&+](\d{1,3})(?:[vV](\d))?" + regexMatchOnlyEnd;
      var match = Regex.Match(word, regexPattern);
      if (match.Success)
      {
        var lowerBound = match.Groups[1].Value;
        var upperBound = match.Groups[3].Value;

        /** Avoid matching expressions such as "009-1" or "5-2" */
        if (StringHelper.StringToInt(lowerBound) < StringHelper.StringToInt(upperBound))
        {
          if (SetEpisodeNumber(lowerBound, token, true))
          {
            SetEpisodeNumber(upperBound, token, true);
            if (!string.IsNullOrEmpty(match.Groups[2].Value))
            {
              _parser.Elements.Add(new Element(Element.ElementCategory.ElementReleaseVersion, match.Groups[2].Value));
            }
            if (!string.IsNullOrEmpty(match.Groups[4].Value))
            {
              _parser.Elements.Add(new Element(Element.ElementCategory.ElementReleaseVersion, match.Groups[4].Value));
            }
            return true;
          }
        }
      }

      return false;
    }

    /// <summary>
    /// Match season and episode patterns. e.g. "2x01", "S01E03", "S01-02xE001-150".
    /// </summary>
    /// <param name="word">the word</param>
    /// <param name="token">the token</param>
    /// <returns>true if the token matched</returns>
    public bool MatchSeasonAndEpisodePattern(string word, Token token)
    {
      var regexPattern = regexMatchOnlyStart + @"S?(\d{1,2})(?:-S?(\d{1,2}))?(?:x|[ ._-x]?E)(\d{1,3})(?:-E?(\d{1,3}))?" + regexMatchOnlyEnd;
      var match = Regex.Match(word, regexPattern);
      if (match.Success)
      {
        _parser.Elements.Add(new Element(Element.ElementCategory.ElementAnimeSeason, match.Groups[1].Value));
        if (!string.IsNullOrEmpty(match.Groups[2].Value))
        {
          _parser.Elements.Add(new Element(Element.ElementCategory.ElementAnimeSeason, match.Groups[2].Value));
        }
        SetEpisodeNumber(match.Groups[3].Value, token, false);
        if (!string.IsNullOrEmpty(match.Groups[4].Value))
        {
          SetEpisodeNumber(match.Groups[4].Value, token, false);
        }
        return true;
      }
      return false;
    }

    /// <summary>
    /// Match type and episode. e.g. "ED1", "OP4a", "OVA2".
    /// </summary>
    /// <param name="word">the word</param>
    /// <param name="token">the token</param>
    /// <returns>true if the token matched</returns>
    public bool MatchTypeAndEpisodePattern(string word, Token token)
    {
      var numberBegin = ParserHelper.IndexOfFirstDigit(word);
      var prefix = StringHelper.SubstringWithCheck(word, 0, numberBegin);

      var category = Element.ElementCategory.ElementAnimeType;
      var options = new KeywordOptions();

      if (KeywordManager.Instance.FindAndSet(KeywordManager.Normalize(prefix), ref category, ref options))
      {
        _parser.Elements.Add(new Element(Element.ElementCategory.ElementAnimeType, prefix));
        var number = word.Substring(numberBegin);
        if (MatchEpisodePatterns(number, token) || SetEpisodeNumber(number, token, true))
        {
          var foundIdx = _parser.Tokens.IndexOf(token);
          if (foundIdx != -1)
          {
            token.Content = number;
            _parser.Tokens.Insert(foundIdx, 
              new Token(options.Identifiable ? Token.TokenCategory.Identifier : Token.TokenCategory.Unknown, prefix, token.Enclosed));
          }

          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Match fractional episodes. e.g. "07.5"
    /// </summary>
    /// <param name="word">the word</param>
    /// <param name="token">the token</param>
    /// <returns>true if the token matched</returns>
    public bool MatchFractionalEpisodePattern(string word, Token token)
    {
      if (string.IsNullOrEmpty(word))
      {
        word = "";
      }

      var regexPattern = regexMatchOnlyStart + @"\d+\.5" + regexMatchOnlyEnd;
      var match = Regex.Match(word, regexPattern);
      if (match.Success && SetEpisodeNumber(word, token, true))
      {
        return true;
      }

      return false;
    }

    /// <summary>
    /// Match partial episodes. e.g. "4a", "111C".
    /// </summary>
    /// <param name="word">the word</param>
    /// <param name="token">the token</param>
    /// <returns>true if the token matched</returns>
    public bool MatchPartialEpisodePattern(string word, Token token)
    {
      if (string.IsNullOrEmpty(word)) return false;
      var foundIdx = Enumerable.Range(0, word.Length)
        .DefaultIfEmpty(word.Length)
        .FirstOrDefault(value => !char.IsDigit(word[value]));
      var suffixLength = word.Length - foundIdx;

      Func<int, bool> isValidSuffix = c => (c >= 'A' && c <= 'C') || (c >= 'a' && c <= 'c');

      if (suffixLength == 1 && isValidSuffix(word[foundIdx]) && SetEpisodeNumber(word, token, true))
      {
        return true;
      }

      return false;
    }

    /// <summary>
    /// Match episodes with number signs. e.g. "#01", "#02-03v2"
    /// </summary>
    /// <param name="word">the word</param>
    /// <param name="token">the token</param>
    /// <returns>true if the token matched</returns>
    public bool MatchNumberSignPattern(string word, Token token)
    {
      if (string.IsNullOrEmpty(word) || word[0] != '#') word = "";
      var regexPattern = regexMatchOnlyStart + @"#(\d{1,3})(?:[-~&+](\d{1,3}))?(?:[vV](\d))?" + regexMatchOnlyEnd;
      var match = Regex.Match(word, regexPattern);
      if (match.Success)
      {
        if (SetEpisodeNumber(match.Groups[1].Value, token, true))
        {
          if (!string.IsNullOrEmpty(match.Groups[2].Value))
          {
            SetEpisodeNumber(match.Groups[2].Value, token, false);
          }
          if (!string.IsNullOrEmpty(match.Groups[3].Value))
          {
            _parser.Elements.Add(new Element(Element.ElementCategory.ElementReleaseVersion, match.Groups[3].Value));
          }

          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Match Japanese patterns. e.g. U+8A71 is used as counter for stories, episodes of TV series, etc.
    /// </summary>
    /// <param name="word">the word</param>
    /// <param name="token">the token</param>
    /// <returns>true if the token matched</returns>
    public bool MatchJapaneseCounterPattern(string word, Token token)
    {
      if (string.IsNullOrEmpty(word) || word[word.Length - 1] != '\u8A71') return false;
      var regexPattern = regexMatchOnlyStart + @"(\d{1,3})話" + regexMatchOnlyEnd;
      var match = Regex.Match(word, regexPattern);
      if (match.Success)
      {
        SetEpisodeNumber(match.Groups[1].Value, token, false);
        return true;
      }

      return false;
    }

    // VOLUME MATCHES

    /// <summary>
    /// Attempts to find an episode/season inside a <code>word</code>
    /// </summary>
    /// <param name="word">the word</param>
    /// <param name="token">the token</param>
    /// <returns>true if the word was matched to an episode/season number</returns>
    public bool MatchVolumePatterns(string word, Token token)
    {
      // All patterns contain at least one non-numeric character
      if (StringHelper.IsNumericString(word)) return false;

      word = word.Trim(" -".ToCharArray());

      var numericFront = char.IsDigit(word[0]);
      var numericBack = char.IsDigit(word[word.Length - 1]);

      if (numericFront && numericBack)
      {
        // e.g. "01v2"
        if (MatchSingleVolumePattern(word, token))
        {
          return true;
        }
        // e.g. "01-02", "03-05v2"
        if (MatchMultiVolumePattern(word, token))
        {
          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Match single volume. e.g. "01v2"
    /// </summary>
    /// <param name="word">the word</param>
    /// <param name="token">the token</param>
    /// <returns>true if the token matched</returns>
    public bool MatchSingleVolumePattern(string word, Token token)
    {
      if (string.IsNullOrEmpty(word)) word = "";
      var regexPattern = regexMatchOnlyStart + @"(\d{1,2})[vV](\d)" + regexMatchOnlyEnd;
      var match = Regex.Match(word, regexPattern);
      if (match.Success)
      {
        SetVolumeNumber(match.Groups[1].Value, token, false);
        _parser.Elements.Add(new Element(Element.ElementCategory.ElementReleaseVersion, match.Groups[2].Value));
        return true;
      }

      return false;
    }

    /// <summary>
    /// Match multi-volume. e.g. "01-02", "03-05v2".
    /// </summary>
    /// <param name="word">the word</param>
    /// <param name="token">the token</param>
    /// <returns>true if the token matched</returns>
    public bool MatchMultiVolumePattern(string word, Token token)
    {
      if (string.IsNullOrEmpty(word)) word = "";
      var regexPattern = regexMatchOnlyStart + @"(\d{1,2})[-~&+](\d{1,2})(?:[vV](\d))?" + regexMatchOnlyEnd;
      var match = Regex.Match(word, regexPattern);
      if (match.Success)
      {
        var lowerBound = match.Groups[1].Value;
        var upperBound = match.Groups[2].Value;
        if (StringHelper.StringToInt(lowerBound) < StringHelper.StringToInt(upperBound))
        {
          if (SetVolumeNumber(lowerBound, token, true))
          {
            SetVolumeNumber(upperBound, token, false);
            if (string.IsNullOrEmpty(match.Groups[3].Value))
            {
              _parser.Elements.Add(new Element(Element.ElementCategory.ElementReleaseVersion, match.Groups[3].Value));
            }
            return true;
          }
        }
      }

      return false;
    }

    // SEARCH

    /// <summary>
    /// Searchs for isolated numbers in a list of <code>tokens</code>.
    /// </summary>
    /// <param name="tokens">the list of tokens</param>
    /// <returns>true if an isolated number was found</returns>
    public bool SearchForIsolatedNumbers(List<Result> tokens)
    {
      foreach (var it in tokens)
      {
        if (!it.Token.Enclosed || !_parser.ParseHelper.IsTokenIsolated(it.Pos.Value)) continue;
        if (SetEpisodeNumber(it.Token.Content, it.Token, true)) return true;
      }

      return false;
    }

    /// <summary>
    /// Searches for separated numbers in a list of <code>tokens</code>.
    /// </summary>
    /// <param name="tokens">the list of tokens</param>
    /// <returns>true fi a separated number was found</returns>
    public bool SearchForSeparatedNumbers(List<Result> tokens)
    {
      foreach (var it in tokens)
      {
        Result previousToken = Token.FindPrevToken(_parser.Tokens, it, Token.TokenFlag.FlagNotDelimiter);

        // See if the number has a preceding "-" separator
        if (ParserHelper.IsTokenCategory(previousToken.Token, Token.TokenCategory.Unknown) && ParserHelper.IsDashCharacter(previousToken.Token.Content[0]))
        {
          if (SetEpisodeNumber(it.Token.Content, it.Token, true))
          {
            previousToken.Token.Category = Token.TokenCategory.Identifier;
            return true;
          }
        }
      }

      return false;
    }

    /// <summary>
    /// Searches for episode patterns in a list of <code>tokens</code>.
    /// </summary>
    /// <param name="tokens">the list of tokens</param>
    /// <returns>true if an episode number was found</returns>
    public bool SearchForEpisodePatterns(List<Result> tokens)
    {
      foreach (var it in tokens)
      {
        var numericFront = it.Token.Content.Length > 0 && char.IsDigit(it.Token.Content[0]);

        if (!numericFront)
        {
          // e.g. "EP.1", "Vol.1"
          if (NumberComesAfterPrefix(Element.ElementCategory.ElementEpisodePrefix, it.Token))
          {
            return true;
          }
          if (NumberComesAfterPrefix(Element.ElementCategory.ElementVolumePrefix, it.Token))
          {
            continue;
          }
        }
        else
        {
          // e.g. "8 of 12"
          if (NumberComesBeforeTotalNumber(it.Token, it.Pos.Value))
          {
            return true;
          }
        }

        // Look for other patterns
        if (MatchEpisodePatterns(it.Token.Content, it.Token))
        {
          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Searches for equivalent number in a list of <code>tokens</code>. e.g. 08(114)
    /// </summary>
    /// <param name="tokens">the list of tokens</param>
    /// <returns>true if an equivalent number was found</returns>
    public bool SearchForEquivalentNumbers(List<Result> tokens)
    {
      foreach (var it in tokens)
      {
        // Find number must be isolated.
        if (_parser.ParseHelper.IsTokenIsolated(it.Pos.Value) || !IsValidEpisodeNumber(it.Token.Content))
        {
          continue;
        }

        // Find the first enclosed, non-delimiter token
        Result nextToken = Token.FindNextToken(_parser.Tokens, it.Pos.Value, Token.TokenFlag.FlagNotDelimiter);
        if (!ParserHelper.IsTokenCategory(nextToken, Token.TokenCategory.Bracket)) continue;
        nextToken = Token.FindNextToken(_parser.Tokens, nextToken, Token.TokenFlag.FlagEnclosed,
          Token.TokenFlag.FlagNotDelimiter);
        if (!ParserHelper.IsTokenCategory(nextToken, Token.TokenCategory.Unknown)) continue;

        // Check if it's an isolated number
        if (!_parser.ParseHelper.IsTokenIsolated(nextToken.Pos.Value)
          || !StringHelper.IsNumericString(nextToken.Token.Content)
          || !IsValidEpisodeNumber(nextToken.Token.Content))
        {
          continue;
        }

        var list = new List<Token>
        {
          it.Token, nextToken.Token
        };

        list.Sort((o1, o2) => 
          StringHelper.StringToInt(o1.Content) - StringHelper.StringToInt(o2.Content));
        SetEpisodeNumber(list[0].Content, list[0], false);
        SetAlternativeEpisodeNumber(list[1].Content, list[1]);
        return true;
      }

      return false;
    }

    /// <summary>
    /// Searches for the last number token in a list of <code>tokens</code>
    /// </summary>
    /// <param name="tokens">the list of tokens</param>
    /// <returns>true if the last number token was found</returns>
    public bool SearchForLastNumber(List<Result> tokens)
    {
      for (var i = tokens.Count - 1; i >= 0; i--)
      {
        var it = tokens[i];

        // Assuming that episode number always comes after the title,
        // the first token cannot be what we're looking for
        if (it.Pos.Value == 0) continue;
        if (it.Token.Enclosed) continue;

        // Ignore if it's the first non-enclosed, non-delimiter token
        if (_parser.Tokens.GetRange(0, it.Pos.Value)
          .All(r => r.Enclosed || r.Category == Token.TokenCategory.Delimiter))
        {
          continue;
        }

        var previousToken = Token.FindPrevToken(_parser.Tokens, it, Token.TokenFlag.FlagNotDelimiter);
        if (ParserHelper.IsTokenCategory(previousToken, Token.TokenCategory.Unknown))
        {
          if (previousToken.Token.Content.Equals("Movie", StringComparison.InvariantCultureIgnoreCase) || previousToken.Token.Content.Equals("Part", StringComparison.InvariantCultureIgnoreCase))
          {
            continue;
          }
        }

        // We'll use this number after all
        if (SetEpisodeNumber(it.Token.Content, it.Token, true))
        {
          return true;
        }
      }

      return false;
    }
  }
} 