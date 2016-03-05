using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace Yawn.Speech
{

    public class Alternative
    {
        public string transcript { get; set; }
        public double confidence { get; set; }
    }

    public class Result
    {
        public List<Alternative> alternative { get; set; }
        public double stability { get; set; }
        public bool final { get; set; }
    }

    public class Response
    {
        public List<Result> result { get; set; }
        public int result_index { get; set; }
    }

    class GoogleSpeechRecognizer : DictationEngine
    {
        SpeechEventListener listener;

        String upstreamUrl;
        String downstreamUrl;;

        HttpWebRequest upstreamRequest;
        HttpWebRequest downstreamRequest;

        Thread upstreamThread;
        Thread downstreamThread;

        String api_key;
        String languageCode;

        private Queue<byte[]> datastream;

        public GoogleSpeechRecognizer(String api_key, String languageCode)
        {
            this.api_key=api_key;
            this.languageCode=languageCode;
        }

        String generateRequestKey()
        {

            const ulong kKeepLowBytes = 0x00000000FFFFFFFF;
            const ulong kKeepHighBytes = 0xFFFFFFFF00000000;

            ulong key = ((ulong)DateTime.Now.ToBinary() | kKeepLowBytes) & ((ulong)BitConverter.DoubleToInt64Bits(new Random().NextDouble()) | kKeepHighBytes);

            return String.Format("{0:X}", key);
        }

        public GoogleSpeechRecognizer(String api_key, String languageCode, SpeechEventListener listener) : this(api_key, languageCode)
        {
            setListener(listener);
        }

        public void setListener(SpeechEventListener listener)
        {
            this.listener = listener;
        }

        public void start(int trailingSilenceMS)
        {

            String pair = generateRequestKey();
            upstreamUrl = String.Format("https://www.google.com/speech-api/full-duplex/v1/up?key={0}&pair={1}&lang={2}&maxAlternatives=20&client=chromium&continuous&interim", api_key, pair, languageCode);
            downstreamUrl = String.Format("https://www.google.com/speech-api/full-duplex/v1/down?maxresults=1&key={0}&pair={1}", api_key, pair);
            datastream = new Queue<byte[]>();
            upstreamThread = new Thread(Upstream);
            downstreamThread = new Thread(Downstream);
            isListening = true;
            downstreamThread.Start();
            upstreamThread.Start();
            listener.onEvent(0, "Recognizer Started");

        }

        private bool isListening = false;

        public void stop()
        {
            byte[] silence = new byte[100];
            write(silence);
            datastream = null;
            isListening = false;
        }

        public void write(byte[] data)
        {
            if (!isListening){
                listner.onEvent(-1,"Do not write data while not listening");
                return;
            }
            else if(datastream != null)
            {
                datastream.Enqueue(data);
            }
        }

        private void Upstream()
        {
            upstreamRequest = (HttpWebRequest)HttpWebRequest.Create(upstreamUrl);
            upstreamRequest.Credentials = CredentialCache.DefaultCredentials;
            upstreamRequest.Method = "POST";
            upstreamRequest.SendChunked = true;
            upstreamRequest.ContentType = "audio/l16; rate=16000";

            try
            {
                using (Stream requestStream = upstreamRequest.GetRequestStream())
                {
                    while (isListening && datastream != null)
                    {
                        if (datastream.Count > 0)
                        {

                            byte[] data = datastream.Dequeue();
                            requestStream.Write(data, 0, data.Length);
                        }
                        else Thread.Sleep(10);
                    }
                }
            }
            catch (WebException e)
            {
                Console.Out.WriteLine(e.Message);
                listener.onEvent(-1,e.Message);
            }
            catch (Exception e)
            {

            }
            finally
            {
                upstreamRequest.Abort();
            }
        }

        private void Downstream()
        {
            downstreamRequest = (HttpWebRequest)HttpWebRequest.Create(downstreamUrl);

            downstreamRequest.Method = "GET";
            downstreamRequest.AllowAutoRedirect = true;
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)downstreamRequest.GetResponse())
                {
                    using (StreamReader responseStream = new StreamReader(response.GetResponseStream()))
                    {
                        while (isListening)
                        {
                            String responseString = responseStream.ReadLine();
                            if (responseString == null || responseString == "")
                            {
                                Console.Out.WriteLine("Empty result");
                                continue;
                            }
                            Console.Out.WriteLine(responseString);
                            Response gresponse = JsonConvert.DeserializeObject<Response>(responseString);
                            if (gresponse.result != null && gresponse.result.Count > 0)
                            {
                                if (gresponse.result[0].final == true)
                                {
                                    String transcript = gresponse.result[0].alternative[0].transcript;
                                    listener.onFinalResult(true, transcript);
                                    isListening = false;
                                    break;
                                }
                                else
                                {
                                    String transcript = "";
                                    foreach (var result in gresponse.result)
                                    {
                                        transcript += result.alternative[0].transcript;
                                    }
                                    listener.onPartialResult(true, transcript);
                                    Console.Out.WriteLine(transcript);
                                }
                            }
                        }
                    }
                }

            }
            catch (WebException e)
            {
                Console.WriteLine(e.Message);
                listener.onEvent(-1,e.Message);
            }
            catch (Exception e)
            {

            }
            finally
            {
                downstreamRequest.Abort();
            }
        }
    }
}
