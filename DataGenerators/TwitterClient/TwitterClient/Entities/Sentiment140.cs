//********************************************************* 
// 
//    Copyright (c) Microsoft. All rights reserved. 
//    This code is licensed under the Microsoft Public License. 
//    THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF 
//    ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY 
//    IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR 
//    PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT. 
// 
//*********************************************************

using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Web;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.Serialization.Json;

namespace TwitterClient
{
    public static class Sentiment
    {
        static SentimentScore ss = new SentimentScore();
        public static TwitterPayload ComputeScore(Tweet tweet, string twitterKeywords)
        {
            // ss = Analyze(tweet.Text);
            MLAnalyze(tweet.Text).Wait();
            return new TwitterPayload
            {
                ID = tweet.Id,
                CreatedAt = ParseTwitterDateTime(tweet.CreatedAt),
                UserName = tweet.User != null ? tweet.User.Name : null,
                TimeZone = tweet.User != null ? (tweet.User.TimeZone != null ? tweet.User.TimeZone : "(unknown)") : "(unknown)", 
                ProfileImageUrl = tweet.User != null ? (tweet.User.ProfileImageUrl != null ? tweet.User.ProfileImageUrl : "(unknown)") : "(unknown)",
                Text = tweet.Text,
                Language = tweet.Language != null ? tweet.Language : "(unknown)",
                RawJson = tweet.RawJson,
                SentimentScore = (int)ss.Score,
                Sentiment = ss.Sentiment,
                Topic = DetermineTopc(tweet.Text, twitterKeywords),
                Location = tweet.User.location != null ? tweet.User.location: null,
                hashtag = tweet.entities.hashtags != null ? getHashTags(tweet.entities.hashtags) : null
            };
        }

        public static string getHashTags(List<Hashtag> hashtags)
        {
            string ret = "";
            foreach (Hashtag h in hashtags)
            {
                ret += h.text + " ";
            }
            return ret.TrimEnd();
        }

        static DateTime ParseTwitterDateTime(string p)
        {
            if (p == null)
                return DateTime.Now;
            p = p.Replace("+0000 ", "");
            DateTimeOffset result;

            if (DateTimeOffset.TryParseExact(p, "ddd MMM dd HH:mm:ss yyyy", CultureInfo.GetCultureInfo("en-us").DateTimeFormat, DateTimeStyles.AssumeUniversal, out result))
                return result.DateTime;
            else
                return DateTime.Now;
        }

        public class Value
        {
            public List<string> ColumnNames { get; set; }
            public List<string> ColumnTypes { get; set; }
            public List<List<string>> Values { get; set; }
        }

        public class Output1
        {
            public string type { get; set; }
            public Value value { get; set; }
        }

        public class Results
        {
            public Output1 output1 { get; set; }
        }

        public class RootObject
        {
            public Results Results { get; set; }
            public int ContainerAllocationDurationMs { get; set; }
            public int ExecutionDurationMs { get; set; }
            public bool IsWarmContainer { get; set; }
        }

        public class SentimentScore
        {
            public enum score
            {
                Positive = 4,
                Neutral = 2,
                Negative = 0,
                Undefined = -1
            }

            public score Score {get; set;}
            public string Sentiment { get; set; }
        }

        static SentimentScore Analyze(string textToAnalyze)
        {
            SentimentScore ret = new SentimentScore();
            try
            {
                string url = string.Format("http://www.sentiment140.com/api/classify?text={0}",
                                            HttpUtility.UrlEncode(textToAnalyze, Encoding.UTF8));
                var response = HttpWebRequest.Create(url).GetResponse();

                using (var streamReader = new StreamReader(response.GetResponseStream()))
                {
                    try
                    {
                        // Read from source
                        var line = streamReader.ReadLine();

                        // Parse
                        var jObject = JObject.Parse(line);

                        int polarity = jObject.SelectToken("results", true).SelectToken("polarity", true).Value<int>();
                        switch (polarity)
                        {
                            case 0: ret.Score = SentimentScore.score.Negative;
                                    ret.Sentiment = "Negative";
                                break;
                            case 4: ret.Score = SentimentScore.score.Positive;
                                    ret.Sentiment = "Positive";
                                break;
                            // 2 or others
                            default: ret.Score = SentimentScore.score.Neutral;
                                     ret.Sentiment = "Neutral";
                                break;
                        }
                    }
                    catch (Exception)
                    {
                        ret.Score = SentimentScore.score.Neutral;
                        ret.Sentiment = "Neutral";
                        return ret;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Sentiment calculation FAILED with:/n{0}", e);
                ret.Score = SentimentScore.score.Neutral;
                ret.Sentiment = "Neutral";
                return ret;
            }
            return ret;
        }
        static async Task MLAnalyze(string textToAnalyze)
        {
            using (var client = new HttpClient())
            {
                String DATA = string.Format(@"
                    {{
                    ""Inputs"": {{
                        ""input1"": {{
                             ""ColumnNames"": [
                             ""sentiment_label"",
                             ""tweet_text""
                             ],
                          ""Values"": [
                        [
                          ""0"",
                          ""{0}""
                        ]
                      ]
                    }}
                }},
                  ""GlobalParameters"": {{}}
                }}",textToAnalyze) ;
                const string apiKey = "pabOOJw3Fd9AoaNdV0tntpSricvH2M+NLisYRUPb2I6XN0KztjpFnSjjgJFfJbaR0vL90IlP/7COq+ReU/mNDw=="; // Replace this with the API key for the web service
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                client.BaseAddress = new Uri("https://ussouthcentral.services.azureml.net/workspaces/5bd3a3f5ea8d4fe7844584989e6f6941/services/af43715959124d96bcc35e58962b649c/execute?api-version=2.0&details=true");
                System.Net.Http.HttpContent content = new StringContent(DATA, UTF8Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(client.BaseAddress, content);

                if (response.IsSuccessStatusCode)
                {
                    string result = await response.Content.ReadAsStringAsync();
                    DataContractJsonSerializer jsonSerializer = new DataContractJsonSerializer(typeof(RootObject));

                    byte[] byteArray = Encoding.UTF8.GetBytes(result);
                    var rootObject = (RootObject)jsonSerializer.ReadObject(new MemoryStream(Encoding.UTF8.GetBytes(result)));
                    string t = rootObject.Results.output1.value.Values[0][0];
                  //  string tt = rootObject.Results.output1.value.Values[1][1];
                    
                    switch (rootObject.Results.output1.value.Values[0][0])
                    {
                        case  "negative": ss.Score = SentimentScore.score.Negative;
                            ss.Sentiment = "Negative";
                            break;
                        case "positive": ss.Score = SentimentScore.score.Positive;
                            ss.Sentiment = "Positive";
                            break;
                        // 2 or others
                        default: ss.Score = SentimentScore.score.Neutral;
                            ss.Sentiment = "Neutral";
                            break;
                    }
                }

                else
                {
                    Console.WriteLine("Call to Sentiment Analysis Service Failed");
                }
            }
        }

        /// <summary>
        /// This is a simple text analysis from the twitter text based on some keywords
        /// </summary>
        /// <param name="tweetText"></param>
        /// <param name="keywordFilters"></param>
        /// <returns></returns>
        static string DetermineTopc(string tweetText, string keywordFilters)
        {
            if (string.IsNullOrEmpty(tweetText))
                return string.Empty;

            string subject = string.Empty;

            //keyPhrases are specified in app.config separated by commas.  Can have no leading or trailing spaces.  Example of key phrases in app.config
            //	<add key="twitter_keywords" value="Microsoft, Office, Surface,Windows Phone,Windows 8,Windows Server,SQL Server,SharePoint,Bing,Skype,XBox,System Center"/><!--comma to spit multiple keywords-->
            string[] keyPhrases = keywordFilters.Split(',');

            foreach (string keyPhrase in keyPhrases)
            {
                subject = keyPhrase;

                //a key phrase may have multiple key words, like: Windows Phone.  If this is the case we will only assign it a subject if both words are 
                //included and in the correct order. For example, a tweet will match if "Windows 8" is found within the tweet but will not match if
                // the tweet is "There were 8 broken Windows".  This is not case sensitive

                //Creates one array that breaks the tweet into individual words and one array that breaks the key phrase into individual words.  Within 
                //This for loop another array is created from the tweet that includes the same number of words as the keyphrase.  These are compared.  For example,
                // KeyPhrase = "Microsoft Office" Tweet= "I Love Microsoft Office"  "Microsoft Office" will be compared to "I Love" then "Love Microsoft" and 
                //Finally "Microsoft Office" which will be returned as the subject.  if no match is found "Do Not Include" is returned. 
                string[] KeyChunk = keyPhrase.Trim().Split(' ');
                string[] tweetTextChunk = tweetText.Split(' ');
                string Y;
                for (int i = 0; i <= (tweetTextChunk.Length - KeyChunk.Length); i++)
                {
                    Y = null;
                    for (int j = 0; j <= (KeyChunk.Length - 1); j++)
                    {
                        Y += tweetTextChunk[(i + j)] + " ";
                    }
                    if (Y != null) Y = Y.Trim();
                    if (Y.ToUpper().Contains(keyPhrase.ToUpper()))
                    {
                        return subject;
                    }
                }
            }

            return "Unknown";
        }
    }
}
