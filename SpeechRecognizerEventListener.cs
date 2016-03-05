using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Yawn.Speech
{
    interface SpeechEventListener
    {
        void onEvent(int code, String message);

        void onPartialResult(bool dictation,String result);

        void onFinalResult(bool dictation,String result);

    }
}
