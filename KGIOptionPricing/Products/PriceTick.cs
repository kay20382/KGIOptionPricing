using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace KGIOptionPricing
{
    public class PriceTick
    {

        public static double[] TickSize = { 10, 5, 2.5, 1, 0.5, 0.25, 0.2, 0.125, 0.1, 0.05, 0.025, 0.02, 0.01, 0.005, 0.0025, 0.001, 0.0001, 0.00005, 0.000005, 0.0000005 };
        //取得合法價格
        public static decimal getRegularPx(TickTypeEnum tickType, decimal px, bool goUp)
        {
            decimal rv = px;
            switch (tickType)
            {
                case TickTypeEnum.ttTXO:
                    rv = TXORegularPx(px, goUp);
                    break;
                case TickTypeEnum.ttSTO:
                    rv = STORegularPx(px, goUp);
                    break;
                case TickTypeEnum.ttTFO:
                    rv = TFORegularPx(px, goUp);
                    break;
                case TickTypeEnum.ttTEO:
                    rv = TEORegularPx(px, goUp);
                    break;
                case TickTypeEnum.ttXIO:
                    rv = XIORegularPx(px, goUp);
                    break;
                case TickTypeEnum.ttETF:
                    rv = ETFRegularPx(px, goUp);
                    break;
                case TickTypeEnum.ttSTF:
                    rv = STFRegularPx(px, goUp);
                    break;
                default:
                    rv = DefaultPx(tickType, px, goUp);
                    break;
            }
            return rv;
        }
        public static decimal getRegularPx(string symbolID, decimal px, bool goUp)
        {
            decimal rv = px;
            switch (symbolID)
            {
                case "TXW":
                    rv = TXORegularPx(px, goUp);
                    break;
                case "TXO":
                    rv = TXORegularPx(px, goUp);
                    break;
                case "STO":
                    rv = STORegularPx(px, goUp);
                    break;
                case "TFO":
                    rv = TFORegularPx(px, goUp);
                    break;
                case "TEO":
                    rv = TEORegularPx(px, goUp);
                    break;
                case "GTO":
                    rv = GTORegularPx(px, goUp);
                    break;
                case "XIO":
                    rv = XIORegularPx(px, goUp);
                    break;
                case "TGO":
                    rv = TGORegularPx(px, goUp);
                    break;
            }
            return rv;
        }
        private static decimal STFRegularPx(decimal px, bool goUp)
        {
            if (goUp)
            {
                if (px < (decimal)10)
                    return Math.Ceiling(px * 100) / 100;
                else if (px < 50)
                    return Math.Ceiling(px * 20) / 20;
                else if (px < 100)
                    return Math.Ceiling(px * 10) / 10;
                else if (px < 500)
                    return Math.Ceiling(px * 2) / 2;
                else if (px < 1000)
                    return Math.Ceiling(px);
                else
                    return Math.Ceiling(px / 5) * 5;

            }
            else
            {
                if (px <= (decimal)10)
                    return Math.Floor(px * 100) / 100;
                else if (px <= 50)
                    return Math.Floor(px * 20) / 20;
                else if (px <= 100)
                    return Math.Floor(px * 10) / 10;
                else if (px <= 500)
                    return Math.Floor(px * 2) / 2;
                else if (px <= 1000)
                    return Math.Floor(px);
                else
                    return Math.Floor(px / 5) * 5;

            }
        }
        private static decimal ETFRegularPx(decimal px, bool goUp)
        {
            if (goUp)
            {
                if (px < 50)
                    return Math.Ceiling(px * 100) / 100;
                else
                    return Math.Ceiling(px * 20) / 20;
            }
            else
            {
                if (px <= 50)
                    return Math.Floor(px * 100) / 100;
                else
                    return Math.Floor(px * 20) / 20;
            }
        }
        private static decimal TGORegularPx(decimal px, bool goUp)
        {
            if (goUp)
            {
                return Math.Ceiling(px * 2) / 2;
            }
            else
            {
                return Math.Floor(px * 2) / 2;
            }
        }
        private static decimal XIORegularPx(decimal px, bool goUp)
        {
            if (goUp)
            {
                if (px < 20)
                    return Math.Ceiling(px * 5) / 5;
                else if (px < 100)
                    return Math.Ceiling(px);
                else if (px < 1000)
                    return Math.Ceiling(px / 2) * 2;
                else if (px < 2000)
                    return Math.Ceiling(px / 10) * 10;
                else
                    return Math.Ceiling(px / 20) * 20;

            }
            else
            {
                if (px <= 20)
                    return Math.Floor(px * 5) / 5;
                else if (px <= 100)
                    return Math.Floor(px);
                else if (px <= 1000)
                    return Math.Floor(px / 2) * 2;
                else if (px <= 2000)
                    return Math.Floor(px / 10) * 10;
                else
                    return Math.Floor(px / 20) * 20;

            }
        }
        private static decimal GTORegularPx(decimal px, bool goUp)
        {
            if (goUp)
            {
                if (px < (decimal)0.5)
                    return Math.Ceiling(px * 200) / 200;
                else if (px < (decimal)2.5)
                    return Math.Ceiling(px * 40) / 40;
                else if (px < 25)
                    return Math.Ceiling(px * 20) / 20;
                else if (px < 50)
                    return Math.Ceiling(px * 4) / 4;
                else
                    return Math.Ceiling(px * 2) / 2;

            }
            else
            {
                if (px <= (decimal)0.5)
                    return Math.Floor(px * 200) / 200;
                else if (px <= (decimal)2.5)
                    return Math.Floor(px * 40) / 40;
                else if (px <= 25)
                    return Math.Floor(px * 20) / 20;
                else if (px <= 50)
                    return Math.Floor(px * 4) / 4;
                else
                    return Math.Floor(px * 2) / 2;

            }
        }
        private static decimal TEORegularPx(decimal px, bool goUp)
        {
            if (goUp)
            {
                if (px < (decimal)0.5)
                    return Math.Ceiling(px * 200) / 200;
                else if (px < (decimal)2.5)
                    return Math.Ceiling(px * 40) / 40;
                else if (px < 25)
                    return Math.Ceiling(px * 20) / 20;
                else if (px < 50)
                    return Math.Ceiling(px * 4) / 4;
                else
                    return Math.Ceiling(px * 2) / 2;

            }
            else
            {
                if (px <= (decimal)0.5)
                    return Math.Floor(px * 200) / 200;
                else if (px <= (decimal)2.5)
                    return Math.Floor(px * 40) / 40;
                else if (px <= 25)
                    return Math.Floor(px * 20) / 20;
                else if (px <= 50)
                    return Math.Floor(px * 4) / 4;
                else
                    return Math.Floor(px * 2) / 2;

            }
        }
        private static decimal TFORegularPx(decimal px, bool goUp)
        {
            if (goUp)
            {
                if (px < 2)
                    return Math.Ceiling(px * 50) / 50;
                else if (px < 10)
                    return Math.Ceiling(px * 10) / 10;
                else if (px < 100)
                    return Math.Ceiling(px * 5) / 5;
                else if (px < 200)
                    return Math.Ceiling(px);
                else
                    return Math.Ceiling(px / 2) * 2;

            }
            else
            {
                if (px <= 2)
                    return Math.Floor(px * 50) / 50;
                else if (px <= 10)
                    return Math.Floor(px * 10) / 10;
                else if (px <= 100)
                    return Math.Floor(px * 5) / 5;
                else if (px <= 200)
                    return Math.Floor(px);
                else
                    return Math.Floor(px / 2) * 2;

            }
        }
        private static decimal TXORegularPx(decimal px, bool goUp)
        {
            if (goUp)
            {
                if (px < 10)
                    return Math.Ceiling(px * 10) / 10;
                else if (px < 50)
                    return Math.Ceiling(px * 2) / 2;
                else if (px < 500)
                    return Math.Ceiling(px);
                else if (px < 1000)
                    return Math.Ceiling(px / 5) * 5;
                else
                    return Math.Ceiling(px / 10) * 10;

            }
            else
            {
                if (px <= 10)
                    return Math.Floor(px * 10) / 10;
                else if (px <= 50)
                    return Math.Floor(px * 2) / 2;
                else if (px <= 500)
                    return Math.Floor(px);
                else if (px <= 1000)
                    return Math.Floor(px / 5) * 5;
                else
                    return Math.Floor(px / 10) * 10;

            }

        }
        private static decimal STORegularPx(decimal px, bool goUp)
        {
            decimal rv = -1;
            if (px < 5)
            {
                rv = goUp ? Math.Ceiling(px * 100) / 100 : Math.Floor(px * 100) / 100;
            }
            else if (px < 15)
            {
                rv = goUp ? Math.Ceiling(px * 20) / 20 : Math.Floor(px * 20) / 20;
            }
            else if (px < 50)
            {
                rv = goUp ? Math.Ceiling(px * 10) / 10 : Math.Floor(px * 10) / 10;
            }
            else if (px < 150)
            {
                rv = goUp ? Math.Ceiling(px * 2) / 2 : Math.Floor(px * 2) / 2;
            }
            else if (px < 1000)
            {
                rv = goUp ? Math.Ceiling(px) : Math.Floor(px);
            }
            else
            {
                rv = goUp ? Math.Ceiling(px / 5) * 5 : Math.Floor(px / 5) * 5;
            }
            return rv;
        }
        private static decimal DefaultPx(TickTypeEnum tickType, decimal px, bool goUp)
        {
            decimal ticks = (decimal)TickSize[(int)tickType];
            if (goUp)
                return Math.Ceiling(px / ticks) * ticks;
            else
                return Math.Floor(px / ticks) * ticks;

        }
        //取得最大報價寬度
        public static decimal getQutPx(string symbolID, decimal px, bool getAsk, bool isFront, bool isRegularPeriod, decimal ulyPx)
        {
            return getQutPx(getQutType(symbolID), px, getAsk, isFront, isRegularPeriod, ulyPx);
        }
        //取得最大報價寬度
        public static decimal getQutPx(QuoteTypeEnum qutType, decimal px, bool getAsk, bool isFront, bool isRegularPeriod, decimal ulyPx)
        {
            decimal rv = 0;
            switch (qutType)
            {
                case QuoteTypeEnum.qtTXO:
                    rv = isFront ? QutTXOFront(px, getAsk) : QutTXONext(px, getAsk);
                    break;
                case QuoteTypeEnum.qtRTO:
                    rv = isFront ? QutRTOFront(px, getAsk, isRegularPeriod) : QutRTONext(px, getAsk, isRegularPeriod);
                    break;
                case QuoteTypeEnum.qtRTF:
                    rv = isFront ? QutRTFFront(px, getAsk, isRegularPeriod) : QutRTFNext(px, getAsk, isRegularPeriod);
                    break;
                case QuoteTypeEnum.qtXEF:
                    rv = QutXEF(px, getAsk, isFront);
                    break;
                case QuoteTypeEnum.qtSPF:
                    rv = QutSPF(px, getAsk, isFront);
                    break;
                case QuoteTypeEnum.qtTGF:
                    rv = QutTGF(px, getAsk);
                    break;
                case QuoteTypeEnum.qtGDF:
                    rv = QutGDF(px, getAsk);
                    break;
                case QuoteTypeEnum.qtI5F:
                    rv = QutI5F(px, getAsk, isFront);
                    break;
                case QuoteTypeEnum.qtTJF:
                    rv = QutTJF(px, getAsk, isFront);
                    break;
                case QuoteTypeEnum.qtT5F:
                    rv = QutT5F(px, getAsk);
                    break;
                case QuoteTypeEnum.qtGTF:
                    rv = QutGTF(px, getAsk);
                    break;
                case QuoteTypeEnum.qtXIF:
                    rv = QutXIF(px, getAsk);
                    break;
                case QuoteTypeEnum.qtSTF:
                    rv = QutSTF(px, getAsk);
                    break;
                case QuoteTypeEnum.qtTGO:
                    rv = QutTGO(px, getAsk, isFront);
                    break;
                case QuoteTypeEnum.qtGTO:
                    rv = QutGTO(px, getAsk, isFront);
                    break;
                case QuoteTypeEnum.qtXIO:
                    rv = QutXIO(px, getAsk, isFront);
                    break;
                case QuoteTypeEnum.qtSTO:
                    rv = QutSTO(px, getAsk, isFront, ulyPx);
                    break;
                case QuoteTypeEnum.qtTEO:
                    rv = QutTEO(px, getAsk, isFront);
                    break;
                case QuoteTypeEnum.qtTFO:
                    rv = QutTFO(px, getAsk, isFront);
                    break;
                case QuoteTypeEnum.qtUDF:
                    rv = QutUDF(px, getAsk, isFront);
                    break;
                case QuoteTypeEnum.qtXJF:

                    break;
                case QuoteTypeEnum.qtGBF:

                    break;
            }

            return rv;
        }
        //道瓊期
        private static decimal QutUDF(decimal px, bool getAsk, bool isFront)
        {
            if (isFront)
            {
                if (getAsk)
                    return px + 6;
                else
                    return px - 6;
            }
            else
            {
                if (getAsk)
                    return px + 10;
                else
                    return px - 10;
            }

        }
        //金融選
        private static decimal QutTFO(decimal px, bool getAsk, bool isFront)
        {
            if (isFront)
            {
                if (getAsk)
                {
                    if (px < 10)
                        return px + (decimal)0.5;
                    else if (px < (decimal)20)
                        return px + 1;
                    else if (px < 40)
                        return px + (decimal)1.4;
                    else
                        return px + 3;

                }
                else
                {
                    if (px <= (decimal)0.52)
                        return (decimal)0.02;
                    else if (px <= (decimal)10.5)
                        return px - (decimal)0.5;
                    else if (px <= 11)
                        return 10;
                    else if (px <= (decimal)21)
                        return px - 1;
                    else if (px <= (decimal)21.4)
                        return (decimal)20;
                    else if (px < (decimal)41.6)
                        return px - (decimal)1.4;
                    else if (px <= 43)
                        return 40;
                    else
                        return px - 3;
                }
            }
            else
            {
                if (getAsk)
                {

                    if (px < 10)
                        return px + 0.8m;
                    else if (px < 20)
                        return px + 1.2m;
                    else if (px < 40)
                        return px + 2.4m;
                    else
                        return px + 4.6m;

                }
                else
                {


                    if (px <= (decimal)0.82)
                        return (decimal)0.02;
                    else if (px <= 10.8m)
                        return px - 0.8m;
                    else if (px <= 11.2m)
                        return 10;
                    else if (px < 21.2m)
                        return px - 1.2m;
                    else if (px < 42.4m)
                        return px - 2.4m;


                    if (px <= (decimal)0.82)
                        return (decimal)0.02;
                    else if (px <= 11)
                        return px - (decimal)0.8;
                    else if (px <= (decimal)11.4)
                        return (decimal)10.2;
                    else if (px <= (decimal)21.4)
                        return px - (decimal)1.2;
                    else if (px <= (decimal)22.6)
                        return (decimal)20.2;
                    else if (px <= (decimal)42.6)
                        return px - (decimal)2.4;
                    else if (px <= (decimal)44.8)
                        return (decimal)40.2;
                    else
                        return px - 4;
                }
            }
        }
        //電子選
        private static decimal QutTEO(decimal px, bool getAsk, bool isFront)
        {
            decimal rv = px;
            if (isFront)
            {
                if (getAsk)
                {
                    if (px < 5)
                        rv = px + (decimal)0.2;
                    else if (px < 10)
                        rv = px + (decimal)0.4;
                    else if (px < 20)
                        rv = px + (decimal)0.8;
                    else
                        rv = px + (decimal)1.6;
                    return getRegularPx("TEO", rv, !getAsk);
                }
                else
                {
                    if (px <= (decimal)0.205)
                        return (decimal)0.005;
                    else if (px <= (decimal)5.2)
                        return px - (decimal)0.2;
                    else if (px <= (decimal)5.45)
                        return (decimal)5.05;
                    else if (px <= (decimal)10.45)
                        return px - (decimal)0.4;
                    else if (px <= (decimal)10.8)
                        return (decimal)10.05;
                    else if (px <= (decimal)20.85)
                        return px - (decimal)0.8;
                    else if (px <= (decimal)21.65)
                        return (decimal)20.05;
                    else
                        return px - (decimal)1.6;
                }
            }
            else
            {
                if (getAsk)
                {
                    if (px < 5)
                        return px + (decimal)0.3;
                    else if (px < 10)
                        return px + (decimal)0.6;
                    else if (px < 20)
                        return px + (decimal)1.2;
                    else
                        return px + (decimal)2.5;

                }
                else
                {
                    if (px <= (decimal)0.305)
                        return (decimal)0.005;
                    else if (px <= (decimal)5.35)
                        return px - (decimal)0.3;
                    else if (px <= (decimal)5.65)
                        return (decimal)5.05;
                    else if (px <= (decimal)10.65)
                        return px - (decimal)0.6;
                    else if (px <= (decimal)10.8)
                        return (decimal)10.05;
                    else if (px <= (decimal)20.85)
                        return px - (decimal)0.8;
                    else if (px <= (decimal)21.65)
                        return (decimal)20.05;
                    else
                        return px - (decimal)1.6;
                }
            }
        }
        //個股選
        private static decimal QutSTO(decimal px, bool getAsk, bool isFront, decimal ulyPx)
        {
            decimal spread = 10;
            if(isFront)
            {
                if(getAsk)
                {
                    if (ulyPx < 50)
                    {
                        if (px <= 1)
                        {
                            spread = 0.1m;
                        }
                        else
                        {
                            spread = px * 0.1m;
                        }
                    }
                    else if (ulyPx >= 50 && ulyPx < 100)
                    {
                        if (px <= 2.5m)
                        {
                            spread = 0.25m;
                        }
                        else
                        {
                            spread = px * 0.1m;
                        }

                    }
                    else if (ulyPx >= 100 && ulyPx < 500)
                    {
                        if (px <= 10)
                        {
                            spread = 1;
                        }
                        else
                        {
                            spread = px * 0.1m;
                        }

                    }
                    else if (ulyPx >= 500 && ulyPx < 1000)
                    {
                        if (px <= 20)
                        {
                            spread = 2;
                        }
                        else
                        {
                            spread = px * 0.1m;
                        }

                    }
                    else if (ulyPx >= 1000)
                    {
                        if (px <= 100)
                        {
                            spread = 10;
                        }
                        else
                        {
                            spread = px * 0.1m;
                        }

                    }
                }
                else //(getAsk)
                {
                    if (ulyPx < 50)
                    {
                        if (px <= 1)
                        {
                            spread = 0.1m;
                        }
                        else
                        {
                            spread = px * 0.090909091m;
                        }
                    }
                    else if (ulyPx >= 50 && ulyPx < 100)
                    {
                        if (px <= 2.5m)
                        {
                            spread = 0.25m;
                        }
                        else
                        {
                            spread = px * 0.090909091m;
                        }

                    }
                    else if (ulyPx >= 100 && ulyPx < 500)
                    {
                        if (px <= 10)
                        {
                            spread = 1;
                        }
                        else
                        {
                            spread = px * 0.090909091m;
                        }

                    }
                    else if (ulyPx >= 500 && ulyPx < 1000)
                    {
                        if (px <= 20)
                        {
                            spread = 2;
                        }
                        else
                        {
                            spread = px * 0.090909091m;
                        }

                    }
                    else if (ulyPx >= 1000)
                    {
                        if (px <= 100)
                        {
                            spread = 10;
                        }
                        else
                        {
                            spread = px * 0.090909091m;
                        }

                    }
                }
            }
            else//(isFront)
            {
                if(getAsk)
                {
                    if (ulyPx < 50)
                    {
                        if (px <= 0.8m)
                        {
                            spread = 0.12m;
                        }
                        else
                        {
                            spread = px * 0.15m;
                        }
                    }
                    else if (ulyPx >= 50 && ulyPx < 100)
                    {
                        if (px <= 2)
                        {
                            spread = 0.3m;
                        }
                        else
                        {
                            spread = px * 0.15m;
                        }

                    }
                    else if (ulyPx >= 100 && ulyPx < 500)
                    {
                        if (px <= 8)
                        {
                            spread = 1.2m;
                        }
                        else
                        {
                            spread = px * 0.15m;
                        }

                    }
                    else if (ulyPx >= 500 && ulyPx < 1000)
                    {
                        if (px <= 20)
                        {
                            spread = 3;
                        }
                        else
                        {
                            spread = px * 0.15m;
                        }

                    }
                    else if (ulyPx >= 1000)
                    {
                        if (px <= 100)
                        {
                            spread = 15;
                        }
                        else
                        {
                            spread = px * 0.15m;
                        }

                    }
                }
                else
                {
                    if (ulyPx < 50)
                    {
                        if (px <= 0.92m)
                        {
                            spread = 0.12m;
                        }
                        else
                        {
                            spread = px * 0.130434783m;
                        }
                    }
                    else if (ulyPx >= 50 && ulyPx < 100)
                    {
                        if (px <= 3.3m)
                        {
                            spread = 0.3m;
                        }
                        else
                        {
                            spread = px * 0.130434783m;
                        }

                    }
                    else if (ulyPx >= 100 && ulyPx < 500)
                    {
                        if (px <= 11.2m)
                        {
                            spread = 1.2m;
                        }
                        else
                        {
                            spread = px * 0.130434783m;
                        }

                    }
                    else if (ulyPx >= 500 && ulyPx < 1000)
                    {
                        if (px <= 23)
                        {
                            spread = 3;
                        }
                        else
                        {
                            spread = px * 0.130434783m;
                        }

                    }
                    else if (ulyPx >= 1000)
                    {
                        if (px <= 115)
                        {
                            spread = 15;
                        }
                        else
                        {
                            spread = px * 0.130434783m;
                        }

                    }
                }
            }

            decimal rv = Math.Max(0.01m, px + (getAsk ? 1 : -1) * spread );

            return STORegularPx(rv, !getAsk);

        }
        //個股選
        //private static decimal QutSTO(decimal px, bool getAsk, bool isFront, decimal ulyPx)
        //{
        //    decimal rv;
        //    if (isFront)
        //    {
        //        if (ulyPx < 50)
        //        {
        //            if (getAsk)
        //                rv = Math.Max(px * (decimal)1.1, px + (decimal)0.1);
        //            else
        //                rv = Math.Max(px / (decimal)1.1, px - (decimal)0.1);
        //        }
        //        else if (ulyPx >= 100)
        //        {
        //            if (getAsk)
        //                rv = Math.Max(px * (decimal)1.1, px + 1);
        //            else
        //                rv = Math.Max(px / (decimal)1.1, px - 1);
        //        }
        //        else
        //        {
        //            if (getAsk)
        //                rv = Math.Max(px * (decimal)1.1, px + (decimal)0.25);
        //            else
        //                rv = Math.Max(px / (decimal)1.1, px - (decimal)0.25);
        //        }
        //    }
        //    else
        //    {
        //        if (ulyPx < 50)
        //        {
        //            if (getAsk)
        //                rv = Math.Max(px * (decimal)1.5, px + (decimal)0.12);
        //            else
        //                rv = Math.Max(px / (decimal)1.5, px - (decimal)0.12);
        //        }
        //        else if (ulyPx >= 100)
        //        {
        //            if (getAsk)
        //                rv = Math.Max(px * (decimal)1.5, px + (decimal)1.2);
        //            else
        //                rv = Math.Max(px / (decimal)1.5, px - (decimal)1.2);
        //        }
        //        else
        //        {
        //            if (getAsk)
        //                rv = Math.Max(px * (decimal)1.5, px + (decimal)0.3);
        //            else
        //                rv = Math.Max(px / (decimal)1.5, px - (decimal)0.3);
        //        }
        //    }

        //    rv = Math.Max((decimal)0.01, rv);

        //    return STORegularPx(rv, !getAsk);
        //}
        //非金電選
        private static decimal QutXIO(decimal px, bool getAsk, bool isFront)
        {
            if (isFront)
            {
                if (getAsk)
                {
                    if (px < 16)
                        return px + 4;
                    else if (px < 17)
                        return 20;
                    else if (px < 18)
                        return 21;
                    else if (px < 19)
                        return 22;
                    else if (px < 20)
                        return 23;
                    else if (px < 50)
                        return px + 4;
                    else if (px < 92)
                        return px + 8;
                    else if (px < 94)
                        return 100;
                    else if (px < 96)
                        return 102;
                    else if (px < 98)
                        return 104;
                    else if (px < 100)
                        return 106;
                    else if (px < 250)
                        return px + 12;
                    else if (px < 976)
                        return px + 24;
                    else if (px < 986)
                        return 1000;
                    else if (px < 996)
                        return 1010;
                    else if (px < 1010)
                        return 1020;
                    else
                        return px + 20;

                }
                else
                {
                    if (px <= (decimal)4.2)
                        return (decimal)0.2;
                    else if (px <= 55)
                        return px - 4;
                    else if (px <= 59)
                        return 51;
                    else if (px <= 110)
                        return px - 8;
                    else if (px <= 114)
                        return 102;
                    else if (px <= 264)
                        return px - 12;
                    else if (px <= 276)
                        return 252;
                    else if (px <= 1020)
                        return px - 24;
                    else
                        return px - 20;
                }

            }
            else
            {
                if (getAsk)
                {
                    if (px < (decimal)12.2)
                        return px + 8;
                    else if (px < 50)
                        return Math.Floor(px) + 8;
                    else if (px < 88)
                        return px + 12;
                    else if (px < 90)
                        return 100;
                    else if (px < 92)
                        return 102;
                    else if (px < 94)
                        return 104;
                    else if (px < 96)
                        return 106;
                    else if (px < 98)
                        return 108;
                    else if (px < 100)
                        return 110;
                    else if (px < 250)
                        return px + 24;
                    else if (px < 952)
                        return px + 48;
                    else if (px < 962)
                        return 1000;
                    else if (px < 972)
                        return 1010;
                    else if (px < 982)
                        return 1020;
                    else if (px < 992)
                        return 1030;
                    else if (px < 1010)
                        return 1040;
                    else
                        return px + 40;

                }
                else
                {
                    if (px <= (decimal)9.2)
                        return (decimal)0.2;
                    else if (px <= 59)
                        return px - 8;
                    else if (px <= 63)
                        return 51;
                    else if (px <= 114)
                        return px - 12;
                    else if (px <= 126)
                        return 102;
                    else if (px <= 276)
                        return px - 24;
                    else if (px <= 300)
                        return 252;
                    else if (px <= 1040)
                        return px - 48;
                    else
                        return px - 40;
                }
            }
        }
        //櫃買選
        private static decimal QutGTO(decimal px, bool getAsk, bool isFront)
        {
            if (isFront)
            {
                if (getAsk)
                {
                    if (px < (decimal)0.305)
                        return px + (decimal)0.2;
                    else if (px < (decimal)0.325)
                        return (decimal)0.5;
                    else if (px < (decimal)0.35)
                        return (decimal)0.525;
                    else if (px < (decimal)0.375)
                        return (decimal)0.55;
                    else if (px < (decimal)0.4)
                        return (decimal)0.575;
                    else if (px < (decimal)0.425)
                        return (decimal)0.6;
                    else if (px < (decimal)0.45)
                        return (decimal)0.625;
                    else if (px < (decimal)0.475)
                        return (decimal)0.65;
                    else if (px < (decimal)0.5)
                        return (decimal)0.675;
                    else if (px < (decimal)2.3)
                        return px + (decimal)0.2;
                    else if (px < (decimal)2.35)
                        return (decimal)2.5;
                    else if (px < (decimal)2.4)
                        return (decimal)2.55;
                    else if (px < (decimal)2.45)
                        return (decimal)2.6;
                    else if (px < 5)
                        return px + (decimal)0.3;
                    else if (px < 10)
                        return px + (decimal)0.6;
                    else if (px < (decimal)23.8)
                        return px + (decimal)1.2;
                    else if (px < (decimal)24.05)
                        return 25;
                    else if (px < (decimal)24.3)
                        return (decimal)25.25;
                    else if (px < (decimal)24.55)
                        return (decimal)25.5;
                    else if (px < (decimal)24.8)
                        return (decimal)25.75;
                    else if (px < 25)
                        return 26;
                    else if (px < 49)
                        return px + 1;
                    else if (px < (decimal)49.5)
                        return 50;
                    else if (px < 50)
                        return (decimal)50.5;
                    else
                        return px + 1;

                }
                else
                {
                    if (px <= (decimal)0.205)
                        return (decimal)0.005;
                    else if (px <= (decimal)2.7)
                        return px - (decimal)0.2;
                    else if (px <= (decimal)2.85)
                        return (decimal)2.55;
                    else if (px <= (decimal)5.35)
                        return px - (decimal)0.3;
                    else if (px <= (decimal)5.65)
                        return (decimal)5.05;
                    else if (px <= (decimal)10.65)
                        return px - (decimal)0.6;
                    else if (px <= (decimal)11.25)
                        return (decimal)10.05;
                    else if (px <= 26)
                        return px - (decimal)1.2;
                    else
                        return px - 1;


                }
            }
            else
            {
                if (getAsk)
                {
                    if (px < (decimal)0.105)
                        return px + (decimal)0.4;
                    else if (px < (decimal)0.125)
                        return (decimal)0.5;
                    else if (px < (decimal)0.15)
                        return (decimal)0.525;
                    else if (px < (decimal)0.175)
                        return (decimal)0.55;
                    else if (px < (decimal)0.2)
                        return (decimal)0.575;
                    else if (px < (decimal)0.225)
                        return (decimal)0.6;
                    else if (px < (decimal)0.25)
                        return (decimal)0.625;
                    else if (px < (decimal)0.275)
                        return (decimal)0.65;
                    else if (px < (decimal)0.3)
                        return (decimal)0.675;
                    else if (px < (decimal)0.325)
                        return (decimal)0.7;
                    else if (px < (decimal)0.375)
                        return (decimal)0.75;
                    else if (px < (decimal)0.4)
                        return (decimal)0.775;
                    else if (px < (decimal)0.425)
                        return (decimal)0.8;
                    else if (px < (decimal)0.45)
                        return (decimal)0.825;
                    else if (px < (decimal)0.475)
                        return (decimal)0.85;
                    else if (px < (decimal)0.5)
                        return (decimal)0.875;

                    else if (px < (decimal)2.1)
                        return px + (decimal)0.4;

                    else if (px < (decimal)2.15)
                        return (decimal)2.5;
                    else if (px < (decimal)2.2)
                        return (decimal)2.55;
                    else if (px < (decimal)2.25)
                        return (decimal)2.6;
                    else if (px < (decimal)2.3)
                        return (decimal)2.65;
                    else if (px < (decimal)2.35)
                        return (decimal)2.7;
                    else if (px < (decimal)2.4)
                        return (decimal)2.75;
                    else if (px < (decimal)2.45)
                        return (decimal)2.8;
                    else if (px < (decimal)2.5)
                        return (decimal)2.85;

                    else if (px < 5)
                        return px + (decimal)0.6;

                    else if (px < 10)
                        return px + (decimal)1.2;

                    else if (px < (decimal)22.6)
                        return px + (decimal)2.4;
                    else if (px < (decimal)22.85)
                        return 25;
                    else if (px < (decimal)23.1)
                        return (decimal)25.25;
                    else if (px < (decimal)23.35)
                        return (decimal)25.5;
                    else if (px < (decimal)23.6)
                        return (decimal)25.75;
                    else if (px < (decimal)23.85)
                        return 26;
                    else if (px < (decimal)24.1)
                        return (decimal)26.25;
                    else if (px < (decimal)24.35)
                        return (decimal)26.5;
                    else if (px < (decimal)24.6)
                        return (decimal)26.75;
                    else if (px < (decimal)25.25)
                        return (decimal)27.25;

                    else if (px < (decimal)47.75)
                        return px + (decimal)2.25;

                    else if (px < (decimal)48.25)
                        return 50;
                    else if (px < (decimal)48.75)
                        return (decimal)50.5;
                    else if (px < (decimal)49.25)
                        return 51;
                    else if (px < (decimal)49.75)
                        return (decimal)51.5;
                    else if (px < (decimal)50.5)
                        return 52;
                    else
                        return px + 2;
                }
                else
                {
                    if (px <= (decimal)0.405)
                        return (decimal)0.005;
                    else if (px <= (decimal)2.95)
                        return px - (decimal)0.4;
                    else if (px <= (decimal)3.15)
                        return (decimal)2.55;
                    else if (px <= (decimal)5.65)
                        return px - (decimal)0.6;
                    else if (px <= (decimal)6.25)
                        return (decimal)5.05;
                    else if (px <= (decimal)11.3)
                        return px - (decimal)1.2;
                    else if (px <= (decimal)12.45)
                        return (decimal)10.05;
                    else if (px <= (decimal)27.25)
                        return px - (decimal)2.4;
                    else
                        return px - 2;

                }

            }
        }
        //黃金選
        private static decimal QutTGO(decimal px, bool getAsk, bool isFront)
        {
            if (isFront)
            {
                if (getAsk)
                {
                    if (px < 50)
                        return px + 3;
                    else if (px < 100)
                        return px + 5;
                    else if (px < 250)
                        return px + 8;
                    else
                        return px + 15;
                }
                else
                {
                    if (px <= (decimal)3.5)
                        return (decimal)0.5;
                    else if (px <= 53)
                        return px - 3;
                    else if (px <= 55)
                        return 50;
                    else if (px <= 105)
                        return px - 5;
                    else if (px <= 108)
                        return (decimal)100;
                    else if (px < 258)
                        return px - 8;
                    else if (px < 265)
                        return 250;
                    else
                        return px - 15;
                }
            }
            else
            {
                if (getAsk)
                {
                    if (px < 50)
                        return px + 5;
                    else if (px < 100)
                        return px + 8;
                    else if (px < 250)
                        return px + 15;
                    else
                        return px + 25;
                }
                else
                {
                    if (px <= (decimal)5.5)
                        return (decimal)0.5;
                    else if (px <= 55)
                        return px - 5;
                    else if (px <= 58)
                        return 50;
                    else if (px <= 108)
                        return px - 8;
                    else if (px <= 115)
                        return 100;
                    else if (px < 265)
                        return px - 15;
                    else if (px < 275)
                        return 250;
                    else
                        return px - 25;
                }
            }
        }
        //個股期
        private static decimal QutSTF(decimal px, bool getAsk)
        {
            decimal next = px;
            while (true)
            {
                if (Math.Abs(next - px) / (getAsk ? px : next) > (decimal)0.01)
                    break;
                else
                    next = getNextTick(next, "STF", getAsk);
            }
            return getNextTick(next, "STF", !getAsk);
        }
        //非金電期
        private static decimal QutXIF(decimal px, bool getAsk)
        {
            if (getAsk)
                return px + 15;
            else
                return px - 15;
        }
        //櫃買期
        private static decimal QutGTF(decimal px, bool getAsk)
        {
            if (getAsk)
                return px + 10;
            else
                return px - 10;
        }
        //台灣50期
        private static decimal QutT5F(decimal px, bool getAsk)
        {
            if (getAsk)
                return px + 15;
            else
                return px - 15;
        }
        //東證期
        private static decimal QutTJF(decimal px, bool getAsk, bool isFront)
        {
            if (getAsk)
                return px + (isFront ? 4 : 6);
            else
                return px - (isFront ? 4 : 6);
        }
        //印度期
        private static decimal QutI5F(decimal px, bool getAsk, bool isFront)
        {
            if (getAsk)
                return px + (isFront ? 5 : 7);
            else
                return px - (isFront ? 5 : 7);
        }
        //美元黃金期
        private static decimal QutGDF(decimal px, bool getAsk)
        {
            if (getAsk)
                return px + 12 * (decimal)0.1;
            else
                return px - 12 * (decimal)0.1;
        }
        //台幣黃金期
        private static decimal QutTGF(decimal px, bool getAsk)
        {
            if (getAsk)
                return px + 12 * (decimal)0.5;
            else
                return px - 12 * (decimal)0.5;
        }
        //S&P期貨
        private static decimal QutSPF(decimal px, bool getAsk, bool isFront)
        {
            if (isFront)
            {
                if (getAsk)
                    return px + 6 * (decimal)0.25;
                else
                    return px - 6 * (decimal)0.25;
            }
            else
            {
                if (getAsk)
                    return px + 10 * (decimal)0.25;
                else
                    return px - 10 * (decimal)0.25;
            }

        }
        //歐元期貨
        private static decimal QutXEF(decimal px, bool getAsk, bool isFront)
        {
            if (getAsk)
                return px + (isFront ? 6 : 10) * (decimal)0.0001;
            else
                return px - (isFront ? 6 : 10) * (decimal)0.0001;
        }
        //其他季月 人民幣期
        private static decimal QutRTFNext(decimal px, bool getAsk, bool isRegularPeriod)
        {
            if (isRegularPeriod)
            {
                if (getAsk)
                    return px + 40 * (decimal)0.0001;
                else
                    return px - 40 * (decimal)0.0001;
            }
            else
            {
                if (getAsk)
                    return px + 100 * (decimal)0.0001;
                else
                    return px - 100 * (decimal)0.0001;
            }
        }
        //近三月 人民幣期
        private static decimal QutRTFFront(decimal px, bool getAsk, bool isRegularPeriod)
        {
            if (isRegularPeriod)
            {
                if (getAsk)
                    return px + 30 * (decimal)0.0001;
                else
                    return px - 30 * (decimal)0.0001;
            }
            else
            {
                if (getAsk)
                    return px + 60 * (decimal)0.0001;
                else
                    return px - 60 * (decimal)0.0001;
            }
        }
        public static void getTXOQut(ref decimal bidPx, ref decimal askPx)
        {
            decimal midPx = (decimal)0.5 * (bidPx + askPx);

            bidPx = (decimal)18.5;
            askPx = (decimal)21.5;

        }
        //近三月 人民幣選
        public static decimal QutRTOFront(decimal px, bool getAsk, bool isRegularPeriod)
        {
            if (isRegularPeriod)
            {
                decimal rv = px * (decimal)0.3;
                rv = Math.Max(rv, (decimal)0.005);
                rv = Math.Floor(rv * 10000) * 10000;
                if (getAsk)
                    return px + rv;
                else
                    return px - rv;

            }
            else
            {
                decimal rv = px * (decimal)0.45;
                rv = Math.Max(rv, (decimal)0.0075);
                rv = Math.Floor(rv * 10000) * 10000;
                if (getAsk)
                    return px + rv;
                else
                    return px - rv;

            }

        }
        //其他季月 人民幣選
        public static decimal QutRTONext(decimal px, bool getAsk, bool isRegular)
        {
            if (isRegular)
            {
                decimal rv = px * (decimal)0.1;
                rv = Math.Max(rv, (decimal)0.005);
                rv = Math.Floor(rv * 10000) * 10000;
                if (getAsk)
                    return px + rv;
                else
                    return px - rv;

            }
            else
            {
                decimal rv = px * (decimal)0.2;
                rv = Math.Max(rv, (decimal)0.01);
                rv = Math.Floor(rv * 10000) * 10000;
                if (getAsk)
                    return px + rv;
                else
                    return px - rv;

            }
        }
        //近月TXO
        public static decimal QutTXOFront(decimal px, bool getAsk)
        {
            if (getAsk)
            {
                if (px < 8)
                    return px + 2;
                else if (px < (decimal)8.6)
                    return 10;
                else if (px < (decimal)9.1)
                    return (decimal)10.5;
                else if (px < (decimal)9.6)
                    return (decimal)11;
                else if (px < 10)
                    return (decimal)11.5;
                else if (px < 15)
                    return px + 2;
                else if (px < 47)
                    return px + 3;
                else if (px < 48)
                    return 50;
                else if (px < 49)
                    return 51;
                else if (px < 50)
                    return 52;
                else if (px < 100)
                    return px + 5;
                else if (px < 250)
                    return px + 8;
                else if (px < 491)
                    return px + 10;
                else if (px < 495)
                    return 500;
                else if (px < 500)
                    return 505;
                else if (px < 1000)
                    return px + 15;
                else
                    return px + 20;

            }
            else
            {
                if (px <= (decimal)2.1)
                    return (decimal)0.1;
                else if (px <= (decimal)17)
                    return px - 2;
                else if (px <= 18)
                    return (decimal)15;
                else if (px <= 53)
                    return px - 3;
                else if (px <= 55)
                    return 50;
                else if (px <= 105)
                    return px - 5;
                else if (px <= 108)
                    return 100;
                else if (px <= 258)
                    return px - 8;
                else if (px <= 261)
                    return 250;
                else if (px <= 510)
                    return px -10;
                else if (px <= 1020)
                    return px - 15;
                else if (px == 1030)
                    return 1000;
                else
                    return px - 20;
            }

        }
        //次近月TXO
        public static decimal QutTXONext(decimal px, bool getAsk)
        {
            if (getAsk)
            {
                if (px < 15)
                    return px + 4;
                else if (px < 50)
                    return px + 5;
                else if (px < 100)
                    return px + 8;
                else if (px < 250)
                    return px + 15;
                else if (px < 50)
                    return px + 20;
                else if (px < 1000)
                    return px + 30;
                else
                    return px + 40;

            }
            else
            {
                if (px <= (decimal)4.1)
                    return (decimal)0.1;
                else if (px <= 19)
                    return px - 4;
                else if (px <= 20)
                    return (decimal)15;
                else if (px <= 55)
                    return px - 5;
                else if (px <= 58)
                    return 50;
                else if (px <= 108)
                    return px - 8;
                else if (px <= 115)
                    return 100;
                else if (px <= 265)
                    return px - 15;
                else if (px <= 270)
                    return 250;
                else if (px <= 520)
                    return px -20;
                else if (px <= 530)
                    return 500;
                
                else if (px <= 1030)
                    return px - 30;
                else if (px <= 1040)
                    return 1000;
                else
                    return px - 40;
            }

        }
        //TXO季月
        public static decimal QutTXOQuarter(decimal px, bool getAsk)
        {
            if (getAsk)
            {
                if (px < 15)
                    return px + 8;
                else if (px < 50)
                    return px + 12;
                else if (px < 100)
                    return px + 20;
                else if (px < 250)
                    return px + 30;
                else
                    return px + 40;

            }
            else
            {
                if (px <= (decimal)8.1)
                    return (decimal)0.1;
                else if (px <= (decimal)23.5)
                    return px - 8;
                else if (px <= (decimal)27.5)
                    return (decimal)15.5;
                else if (px <= 63)
                    return px - 12;
                else if (px <= 71)
                    return 50;
                else if (px <= 121)
                    return px - 20;
                else if (px <= 131)
                    return 100;
                else if (px <= 281)
                    return px - 30;
                else if (px <= 291)
                    return 250;
                else
                    return px - 40;
            }
        }
        //取得價格序列
        public static List<decimal> getPrcList(TickTypeEnum tickType, decimal UpLimit, decimal DnLimit)
        {
            List<decimal> rv = new List<decimal>();

            decimal next = UpLimit;
            while (next >= DnLimit)
            {
                rv.Add(next);
                next = getNextTick(next, tickType, false);
            }
            return rv;
        }
        //取得價格序列
        public static List<double> getPrcListDouble(TickTypeEnum tickType, double UpLimit, double DnLimit)
        {
            List<double> rv = new List<double>();

            double next = UpLimit;
            while (next >= DnLimit)
            {
                rv.Add(next);
                next = (double)getNextTick((decimal)next, tickType, false);
            }
            return rv;
        }
        //取得小數位數
        public static int getPrcDigital(string symbolID)
        {
            int tt = 0;
            switch (symbolID)
            {
                // JPX 日本交易所商品
                case "225":
                    tt = 0;
                    break;
                case "225M":
                case "225MC":
                    tt = 0;
                    break;
                case "TPXM":
                    tt = 2;
                    break;
                case "TPX":
                    tt = 1;
                    break;
                case "G2F":
                    tt = 0;
                    break;
                case "SSI":
                    tt = 1;
                    break;
                case "STW":
                    tt = 1;
                    break;
                case "1GC":
                    tt = 1;
                    break;
                case "TXW":
                    tt = 1;
                    break;
                case "MXW":
                    tt = 0;
                    break;
                case "TXO":
                    tt = 1;
                    break;
                case "STF":
                    tt = 2;
                    break;
                case "STO":
                    tt = 2;
                    break;
                case "ETF":
                    tt = 2;
                    break;
                case "TEO":
                    tt = 3;
                    break;
                case "GTO":
                    tt = 3;
                    break;
                case "TFO":
                    tt = 2;
                    break;
                case "XIO":
                    tt = 1;
                    break;
                case "TXF":
                    tt = 0;
                    break;
                case "T5F":
                    tt = 0;
                    break;
                case "XIF":
                    tt = 0;
                    break;
                case "I5F":
                    tt = 0;
                    break;
                case "UDF":
                    tt = 0;
                    break;
                case "TGF":
                    tt = 1;
                    break;
                case "TGO":
                    tt = 1;
                    break;
                case "TJF":
                    tt = 2;
                    break;
                case "SPF":
                    tt = 2;
                    break;
                case "FXF":
                    tt = 1;
                    break;
                case "GDF":
                    tt = 1;
                    break;
                case "EXF":
                    tt = 2;
                    break;
                case "GTF":
                    tt = 2;
                    break;
                case "XJF":
                    tt = 2;
                    break;
                case "GBF":
                    tt = 3;
                    break;
                case "RTF":
                    tt = 4;
                    break;
                case "RHF":
                    tt = 4;
                    break;
                case "XEF":
                    tt = 3;
                    break;
                case "RTO":
                    tt = 3;
                    break;
                case "RHO":
                    tt = 3;
                    break;
                default:
                    tt = 0;
                    break;
            }
            return tt;

        }
        //取得價格序列
        public static List<decimal> getPrcList(string symbolID, decimal UpLimit, decimal DnLimit)
        {
            return getPrcList(getTickType(symbolID), UpLimit, DnLimit);
        }
        //取得下一個TICK
        public static decimal getNextTick(decimal px, string symbolID, bool goUp)
        {
            return getNextTick(px, getTickType(symbolID), goUp);
        }
        //商品轉換
        public static TickTypeEnum getTickType(string symbolID)
        {
            TickTypeEnum tt = new TickTypeEnum();
            switch (symbolID)
            {
                // JPX 日本交易所商品
                case "225":
                    tt = TickTypeEnum.tt100;
                    break;
                case "225M":
                case "225MC":
                    tt = TickTypeEnum.tt50;
                    break;
                case "TPXM":
                    tt = TickTypeEnum.tt025;
                    break;
                case "TPX":
                    tt = TickTypeEnum.tt05;
                    break;
                case "G2F":
                    tt = TickTypeEnum.tt1;
                    break;
                case "SSI":
                    tt = TickTypeEnum.tt50;
                    break;
                case "STW":
                    tt = TickTypeEnum.tt01;
                    break;
                case "1GC":
                    tt = TickTypeEnum.tt01;
                    break;
                case "GC":
                case "MGC":
                    tt = TickTypeEnum.tt01;
                    break;
                case "SI":
                case "SIL":
                    tt = TickTypeEnum.tt0005;
                    break;
                case "SCN":
                    tt = TickTypeEnum.tt1;
                    break;
                case "TXW":
                    tt = TickTypeEnum.ttTXO;
                    break;
                case "MXW":
                    tt = TickTypeEnum.tt1;
                    break;
                case "MX1":
                    tt = TickTypeEnum.tt1;
                    break;
                case "MX2":
                    tt = TickTypeEnum.tt1;
                    break;
                case "MX3":
                    tt = TickTypeEnum.tt1;
                    break;
                case "MX4":
                    tt = TickTypeEnum.tt1;
                    break;
                case "MX5":
                    tt = TickTypeEnum.tt1;
                    break;
                case "TX1":
                case "TX2":
                case "TX3":
                case "TX5":
                case "TXU":
                case "TXV":
                case "TXX":
                case "TXY":
                case "TXZ":
                case "TXO":
                    tt = TickTypeEnum.ttTXO;
                    break;
                case "STF":
                    tt = TickTypeEnum.ttSTF;
                    break;
                case "STO":
                    tt = TickTypeEnum.ttSTO;
                    break;
                case "ETF":
                    tt = TickTypeEnum.ttETF;
                    break;
                case "TEO":
                    tt = TickTypeEnum.ttTEO;
                    break;
                case "GTO":
                    tt = TickTypeEnum.ttTEO;
                    break;
                case "TFO":
                    tt = TickTypeEnum.ttTFO;
                    break;
                case "XIO":
                    tt = TickTypeEnum.ttXIO;
                    break;
                case "TXF":
                    tt = TickTypeEnum.tt1;
                    break;
                case "T5F":
                    tt = TickTypeEnum.tt1;
                    break;
                case "XIF":
                    tt = TickTypeEnum.tt1;
                    break;
                case "I5F":
                    tt = TickTypeEnum.tt1;
                    break;
                case "UDF":
                    tt = TickTypeEnum.tt1;
                    break;
                case "TGF":
                    tt = TickTypeEnum.tt05;
                    break;
                case "TGO":
                    tt = TickTypeEnum.tt05;
                    break;
                case "TJF":
                    tt = TickTypeEnum.tt025;
                    break;
                case "SPF":
                    tt = TickTypeEnum.tt025;
                    break;
                case "FXF":
                    tt = TickTypeEnum.tt02;
                    break;
                case "GDF":
                    tt = TickTypeEnum.tt01;
                    break;
                case "EXF":
                    tt = TickTypeEnum.tt005;
                    break;
                case "GTF":
                    tt = TickTypeEnum.tt005;
                    break;
                case "XJF":
                    tt = TickTypeEnum.tt001;
                    break;
                case "GBF":
                    tt = TickTypeEnum.tt0005;
                    break;
                case "RTF":
                    tt = TickTypeEnum.tt00001;
                    break;
                case "RHF":
                    tt = TickTypeEnum.tt00001;
                    break;
                case "XEF":
                    tt = TickTypeEnum.tt0005;
                    break;
                case "RTO":
                    tt = TickTypeEnum.tt0005;
                    break;
                case "RHO":
                    tt = TickTypeEnum.tt0005;
                    break;

                case "ES":
                case "E1C":
                case "E2C":
                case "E3C":
                case "E4C":
                    tt = TickTypeEnum.tt025;
                    break;

                default:
                    tt = TickTypeEnum.tt1;
                    break;
            }
            return tt;
        }
        //商品轉換
        public static QuoteTypeEnum getQutType(string symbolID)
        {
            QuoteTypeEnum qt = new QuoteTypeEnum();
            switch (symbolID)
            {
                case "CAF":
                case "CBF":
                case "CCF":
                case "CDF":
                case "CEF":
                case "CFF":
                case "CGF":
                case "CHF":
                case "CJF":
                case "CKF":
                case "CLF":
                case "CMF":
                case "CNF":
                case "CQF":
                case "CRF":
                case "CSF":
                case "CUF":
                case "CWF":
                case "CXF":
                case "CYF":
                case "CZF":
                case "DCF":
                case "DDF":
                case "DEF":
                case "DFF":
                case "DGF":
                case "DHF":
                case "DIF":
                case "DJF":
                case "DKF":
                case "DLF":
                case "DNF":
                case "DOF":
                case "DPF":
                case "DQF":
                case "DSF":
                case "DUF":
                case "DVF":
                case "DWF":
                case "DXF":
                case "DYF":
                case "DZF":
                case "EEF":
                case "EGF":
                case "EHF":
                case "EMF":
                case "EPF":
                case "ERF":
                case "EYF":
                case "EZF":
                case "FEF":
                case "FFF":
                case "FGF":
                case "FKF":
                case "FNF":
                case "FQF":
                case "FRF":
                case "FTF":
                case "FVF":
                case "FWF":
                case "FYF":
                case "FZF":
                case "GAF":
                case "GCF":
                case "GHF":
                case "GIF":
                case "GJF":
                case "GLF":
                case "GMF":
                case "GNF":
                case "GOF":
                case "GRF":
                case "GUF":
                case "GWF":
                case "GXF":
                case "GZF":
                case "HAF":
                case "HBF":
                case "HCF":
                case "HHF":
                case "HIF":
                case "HLF":
                case "HOF":
                case "HSF":
                case "HYF":
                case "IAF":
                case "IHF":
                case "IIF":
                case "IJF":
                case "IMF":
                case "IOF":
                case "IPF":
                case "IQF":
                case "IRF":
                case "ITF":
                case "IXF":
                case "IYF":
                case "IZF":
                case "JBF":
                case "JFF":
                case "JMF":
                case "JNF":
                case "JPF":
                case "JSF":
                case "JWF":
                case "JZF":
                case "KAF":
                case "KBF":
                case "KCF":
                case "KDF":
                case "KEF":
                case "KFF":
                case "KGF":
                case "KIF":
                case "KKF":
                case "KLF":
                case "KOF":
                case "KPF":
                case "KSF":
                case "KWF":
                case "LBF":
                case "LCF":
                case "LEF":
                case "LIF":
                case "LMF":
                case "LOF":
                case "LQF":
                case "LRF":
                case "LTF":
                case "LUF":
                case "LVF":
                case "LWF":
                case "LXF":
                case "LYF":
                case "MAF":
                case "MBF":
                case "MCF":
                case "MJF":
                case "MKF":
                case "MPF":
                case "MQF":
                case "MVF":
                case "MYF":
                case "NAF":
                case "NBF":
                case "NCF":
                case "NDF":
                case "NEF":
                case "NGF":
                case "NIF":
                case "NJF":
                case "NLF":
                case "NMF":
                case "NOF":
                case "NQF":
                case "NSF":
                case "NUF":
                case "NVF":
                case "NWF":
                case "NXF":
                case "NYF":
                case "NZF":
                case "OAF":
                case "OBF":
                case "OCF":
                case "ODF":
                case "OEF":
                case "OHF":
                case "OJF":
                case "OKF":
                case "OLF":
                case "OMF":
                case "OOF":
                case "OPF":
                case "OQF":
                case "ORF":
                case "OSF":
                case "OTF":
                case "OUF":
                case "OVF":
                case "OWF":
                case "OXF":
                case "OYF":
                case "OZF":
                case "PAF":
                case "PBF":
                case "PCF":
                case "PDF":
                case "PEF":
                case "PFF":
                case "PGF":
                case "PHF":
                case "PIF":
                case "PJF":
                case "PKF":
                case "PLF":
                case "PMF":
                case "PNF":
                case "POF":
                case "PPF":
                case "PQF":
                case "PRF":
                case "PSF":
                case "PTF":
                case "PUF":
                case "PVF":
                case "PWF":
                case "PXF":
                case "PYF":
                case "PZF":
                case "QAF":
                    qt = QuoteTypeEnum.qtSTF;
                    break;
                case "CAO":
                case "CBO":
                case "CCO":
                case "CDO":
                case "CEO":
                case "CFO":
                case "CGO":
                case "CHO":
                case "CJO":
                case "CKO":
                case "CLO":
                case "CMO":
                case "CNO":
                case "CQO":
                case "CRO":
                case "CSO":
                case "CXO":
                case "CZO":
                case "DCO":
                case "DEO":
                case "DFO":
                case "DGO":
                case "DHO":
                case "DJO":
                case "DKO":
                case "DLO":
                case "DNO":
                case "DOO":
                case "DPO":
                case "DQO":
                case "DSO":
                case "DUO":
                case "DVO":
                case "DWO":
                case "DXO":
                case "GIO":
                case "GXO":
                case "HCO":
                case "IJO":
                case "LOO":
                case "NYO":
                case "NZO":
                case "OAO":
                case "OBO":
                case "OCO":
                case "OJO":
                case "OKO":
                case "OOO":
                case "OZO":
                    qt = QuoteTypeEnum.qtSTO;
                    break;
                case "TXW":
                case "TX1":
                case "TX2":
                case "TX4":
                case "TX5":
                    qt = QuoteTypeEnum.qtTXO;
                    break;
                case "TXO":
                    qt = QuoteTypeEnum.qtTXO;
                    break;
                case "STF":
                    qt = QuoteTypeEnum.qtSTF;
                    break;
                case "STO":
                    qt = QuoteTypeEnum.qtSTO;
                    break;
                case "TEO":
                    qt = QuoteTypeEnum.qtTEO;
                    break;
                case "GTO":
                    qt = QuoteTypeEnum.qtGTO;
                    break;
                case "TFO":
                    qt = QuoteTypeEnum.qtTFO;
                    break;
                case "XIO":
                    qt = QuoteTypeEnum.qtXIO;
                    break;
                case "T5F":
                    qt = QuoteTypeEnum.qtT5F;
                    break;
                case "XIF":
                    qt = QuoteTypeEnum.qtXIF;
                    break;
                case "I5F":
                    qt = QuoteTypeEnum.qtI5F;
                    break;
                case "UDF":
                    qt = QuoteTypeEnum.qtUDF;
                    break;
                case "TGF":
                    qt = QuoteTypeEnum.qtTGF;
                    break;
                case "TGO":
                    qt = QuoteTypeEnum.qtTGO;
                    break;
                case "TJF":
                    qt = QuoteTypeEnum.qtTJF;
                    break;
                case "SPF":
                    qt = QuoteTypeEnum.qtSPF;
                    break;
                case "GDF":
                    qt = QuoteTypeEnum.qtGDF;
                    break;
                case "GTF":
                    qt = QuoteTypeEnum.qtGTF;
                    break;
                case "XJF":
                    qt = QuoteTypeEnum.qtXJF;
                    break;
                case "GBF":
                    qt = QuoteTypeEnum.qtGBF;
                    break;
                case "RTF":
                    qt = QuoteTypeEnum.qtRTF;
                    break;
                case "RHF":
                    qt = QuoteTypeEnum.qtRTF;
                    break;
                case "XEF":
                    qt = QuoteTypeEnum.qtXEF;
                    break;
                case "RTO":
                    qt = QuoteTypeEnum.qtRTO;
                    break;
                case "RHO":
                    qt = QuoteTypeEnum.qtRTO;
                    break;
                default:
                    qt = QuoteTypeEnum.qtTXO;
                    break;
            }
            return qt;
        }
       
        //取得下一個TICK
        public static decimal getNextTick(decimal px, TickTypeEnum tickType, bool goUp)
        {
            decimal rv = 0;
            switch (tickType)
            {
                case TickTypeEnum.ttTXO:
                    rv = NextTXO(px, goUp);
                    break;
                case TickTypeEnum.ttSTF:
                    rv = NextSTF(px, goUp);
                    break;
                case TickTypeEnum.ttETF:
                    rv = NextETF(px, goUp);
                    break;
                case TickTypeEnum.ttSTO:
                    rv = NextSTO(px, goUp);
                    break;
                case TickTypeEnum.ttTEO:
                    rv = NextTEO(px, goUp);
                    break;
                case TickTypeEnum.ttTFO:
                    rv = NextTFO(px, goUp);
                    break;
                case TickTypeEnum.ttXIO:
                    rv = NextXIO(px, goUp);
                    break;
                default:
                    rv = NextTick(px, tickType, goUp);
                    break;
            }
            return rv;
        }
        //等距TICK
        private static decimal NextTick(decimal px, TickTypeEnum tickType, bool goUp)
        {
            if (goUp)
                px += (decimal)TickSize[(int)tickType];
            else
                px -= (decimal)TickSize[(int)tickType];

            return px;
        }
        //非等距TICK
        private static decimal NextTXO(decimal px, bool goUp)
        {
            if (goUp)
            {
                if (px < 10)
                    return px + (decimal)0.1;
                else if (px < 50)
                    return px + (decimal)0.5;
                else if (px < 500)
                    return px + 1;
                else if (px < 1000)
                    return px + 5;
                else
                    return px + 10;

            }
            else
            {
                if (px <= 10)
                    return px - (decimal)0.1;
                else if (px <= 50)
                    return px - (decimal)0.5;
                else if (px <= 500)
                    return px - 1;
                else if (px <= 1000)
                    return px - 5;
                else
                    return px - 10;
            }
        }
        private static decimal NextSTF(decimal px, bool goUp)
        {
            if (goUp)
            {
                if (px < 10)
                    return px + (decimal)0.01;
                else if (px < 50)
                    return px + (decimal)0.05;
                else if (px < 100)
                    return px + (decimal)0.1;
                else if (px < 500)
                    return px + (decimal)0.5;
                else if (px < 1000)
                    return px + 1;
                else
                    return px + 5;
            }
            else
            {
                if (px <= 10)
                    return px - (decimal)0.01;
                else if (px <= 50)
                    return px - (decimal)0.05;
                else if (px <= 100)
                    return px - (decimal)0.1;
                else if (px <= 500)
                    return px - (decimal)0.5;
                else if (px <= 1000)
                    return px - 1;
                else
                    return px - 5;
            }
        }
        private static decimal NextETF(decimal px, bool goUp)
        {
            if (goUp)
            {
                if (px < 50)
                    return px + (decimal)0.01;
                else
                    return px + (decimal)0.05;
            }
            else
            {
                if (px <= 50)
                    return px - (decimal)0.01;
                else
                    return px - (decimal)0.05;
            }
        }
        private static decimal NextSTO(decimal px, bool goUp)
        {
            if (goUp)
            {
                if (px < 5)
                    return px + (decimal)0.01;
                else if (px < 15)
                    return px + (decimal)0.05;
                else if (px < 50)
                    return px + (decimal)0.1;
                else if (px < 150)
                    return px + (decimal)0.5;
                else if (px < 1000)
                    return px + 1;
                else
                    return px + 5;
            }
            else
            {
                if (px <= 5)
                    return px - (decimal)0.01;
                else if (px <= 15)
                    return px - (decimal)0.05;
                else if (px <= 50)
                    return px - (decimal)0.1;
                else if (px <= 150)
                    return px - (decimal)0.5;
                else if (px <= 1000)
                    return px - 1;
                else
                    return px - 5;
            }
        }
        private static decimal NextTEO(decimal px, bool goUp)
        {
            if (goUp)
            {
                if (px < (decimal)0.5)
                    return px + (decimal)0.005;
                else if (px < (decimal)2.5)
                    return px + (decimal)0.025;
                else if (px < 25)
                    return px + (decimal)0.05;
                else if (px < 50)
                    return px + (decimal)0.25;
                else
                    return px + (decimal)0.5;
            }
            else
            {
                if (px <= (decimal)0.5)
                    return px - (decimal)0.005;
                else if (px <= (decimal)2.5)
                    return px - (decimal)0.025;
                else if (px <= 25)
                    return px - (decimal)0.05;
                else if (px <= 50)
                    return px - (decimal)0.25;
                else
                    return px - (decimal)0.5;
            }
        }
        private static decimal NextTFO(decimal px, bool goUp)
        {
            if (goUp)
            {
                if (px < 2)
                    return px + (decimal)0.02;
                else if (px < 10)
                    return px + (decimal)0.1;
                else if (px < 100)
                    return px + (decimal)0.2;
                else if (px < 200)
                    return px + 1;
                else
                    return px + 2;
            }
            else
            {
                if (px <= 2)
                    return px - (decimal)0.02;
                else if (px <= 10)
                    return px - (decimal)0.1;
                else if (px <= 100)
                    return px - (decimal)0.2;
                else if (px <= 200)
                    return px - 1;
                else
                    return px - 2;
            }
        }
        private static decimal NextXIO(decimal px, bool goUp)
        {
            if (goUp)
            {
                if (px < 20)
                    return px + (decimal)0.2;
                else if (px < 100)
                    return px + 1;
                else if (px < 1000)
                    return px + 2;
                else if (px < 2000)
                    return px + 10;
                else
                    return px + 20;
            }
            else
            {
                if (px <= 20)
                    return px - (decimal)0.2;
                else if (px <= 100)
                    return px - 1;
                else if (px <= 1000)
                    return px - 2;
                else if (px <= 2000)
                    return px - 10;
                else
                    return px - 20;
            }
        }
    }
    public enum TickTypeEnum
    {
        [Description("10")]
        tt100 = 0,//JNI
        [Description("5")]
        tt50 = 1,//SSI
        [Description("2.5")]
        tt25 = 2,//A50
        [Description("1")]
        tt1 = 3, //TXF T5F XIF I5F UDF G2F E4F 
        [Description("0.5")]
        tt05 = 4,//TGF TGO
        [Description("0.25")]
        tt025 = 5,//TJF SPF
        [Description("0.2")]
        tt02 = 6, //FXF
        [Description("0.125")]
        tt0125 = 7,
        [Description("0.1")]
        tt01 = 8,//GDF
        [Description("0.05")]
        tt005 = 9,//EXF GTF
        [Description("0.025")]
        tt0025 = 10,
        [Description("0.02")]
        tt002 = 11,
        [Description("0.01")]
        tt001 = 12,//XJF
        [Description("0.005")]
        tt0005 = 13, //GBF
        [Description("0.0025")]
        tt00025 = 14,
        [Description("0.001")]
        tt0001 = 15, //RTF RHF XEF RTO RHO
        [Description("0.0001")]
        tt00001 = 16, //RTF RHF XEF RTO RHO
        [Description("0.00005")]
        tt000005 = 17,
        [Description("0.000005")]
        tt0000005 = 18,
        [Description("0.0000005")]
        tt00000005 = 19,
        [Description("TXO")]
        ttTXO = 20,//TXO
        [Description("STF")]
        ttSTF = 21,//STF STK
        [Description("ETF")]
        ttETF = 22,//ETF
        [Description("STO")]
        ttSTO = 23,//STO
        [Description("TEO/GTO")]
        ttTEO = 24,//TEO GTO
        [Description("TFO")]
        ttTFO = 25,//TFO
        [Description("XIO")]
        ttXIO = 26//XIO

    }
    public enum QuoteTypeEnum
    {
        [Description("TXO")]
        qtTXO = 0,//TXO
        [Description("RTO")]
        qtRTO = 1,//RHO RTO
        [Description("RTF")]
        qtRTF = 2,//RHF RTF
        [Description("XEF")]
        qtXEF = 3,//XEF XJF
        [Description("SPF")]
        qtSPF = 4, //SPF
        [Description("TGF")]
        qtTGF = 5, //TGF
        [Description("GDF")]
        qtGDF = 6, //GDF
        [Description("I5F")]
        qtI5F = 7, //I5F
        [Description("TJF")]
        qtTJF = 8,
        [Description("T5F")]
        qtT5F = 9,
        [Description("GTF")]
        qtGTF = 10,
        [Description("XIF")]
        qtXIF = 11,
        [Description("STF")]
        qtSTF = 12,
        [Description("TGO")]
        qtTGO = 13,
        [Description("GRO")]
        qtGTO = 14,
        [Description("XIO")]
        qtXIO = 15,
        [Description("STO")]
        qtSTO = 16,
        [Description("TEO")]
        qtTEO = 17,
        [Description("TFO")]
        qtTFO = 18,
        [Description("UDF")]
        qtUDF = 19,
        [Description("XJF")]
        qtXJF = 20,
        [Description("GBF")]
        qtGBF = 21,
        [Description("ETO")]
        qtETO = 22,
        [Description("None")]
        qtNone = 99
    }
}

//namespace VegaTrader4
//{
//    class PriceTick
//    {

//        public static double[] TickSize = {10,5,2.5,1,0.5,0.25,0.2,0.125,0.1,0.05,0.025,0.02,0.01,0.005,0.0025,0.001,0.0001,0.00005,0.000005,0.0000005};
//        //取得合法價格
//        public static decimal getRegularPx(TickTypeEnum tickType, decimal px, bool goUp)
//        {
//            decimal rv = px;
//            switch (tickType)
//            {
//                case TickTypeEnum.ttTXO:
//                    rv = TXORegularPx(px, goUp);
//                    break;
//                case TickTypeEnum.ttSTO:
//                    rv = STORegularPx(px, goUp);
//                    break;
//                case TickTypeEnum.ttTFO:
//                    rv = TFORegularPx(px, goUp);
//                    break;
//                case TickTypeEnum.ttTEO:
//                    rv = TEORegularPx(px, goUp);
//                    break;
//                case TickTypeEnum.ttXIO:
//                    rv = XIORegularPx(px, goUp);
//                    break;
//                case TickTypeEnum.ttETF:
//                    rv = ETFRegularPx(px, goUp);
//                    break;
//                case TickTypeEnum.ttSTF:
//                    rv = STFRegularPx(px, goUp);
//                    break;
//                default:
//                    rv = DefaultPx(tickType, px, goUp);
//                    break;
//            }
//            return rv;
//        }
//        public static decimal getRegularPx(string symbolID, decimal px, bool goUp)
//        {
//            decimal rv = px;
//            switch (symbolID)
//            {
//                case "TXW":
//                    rv = TXORegularPx(px, goUp);
//                    break;
//                case "TXO":
//                case "TX1":
//                case "TX2":
//                case "TX3":
//                case "TX4":
//                case "TX5":
//                    rv = TXORegularPx(px, goUp);
//                    break;
//                case "STO":
//                    rv = STORegularPx(px, goUp);
//                    break;
//                case "TFO":
//                    rv = TFORegularPx(px, goUp);
//                    break;
//                case "TEO":
//                    rv = TEORegularPx(px, goUp);
//                    break;
//                case "GTO":
//                    rv = GTORegularPx(px, goUp);
//                    break;
//                case "XIO":
//                    rv = XIORegularPx(px, goUp);
//                    break;
//                case "TGO":
//                    rv = TGORegularPx(px, goUp);
//                    break;
//            }
//            return rv;
//        }
//        private static decimal STFRegularPx(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                if (px < (decimal)10)
//                    return Math.Ceiling(px * 100) / 100;
//                else if (px < 50)
//                    return Math.Ceiling(px * 20) / 20;
//                else if (px < 100)
//                    return Math.Ceiling(px * 10) / 10;
//                else if (px < 500)
//                    return Math.Ceiling(px * 2) / 2;
//                else if (px < 1000)
//                    return Math.Ceiling(px);
//                else
//                    return Math.Ceiling(px / 5) * 5;

//            }
//            else
//            {
//                if (px <= (decimal)10)
//                    return Math.Floor(px * 100) / 100;
//                else if (px <= 50)
//                    return Math.Floor(px * 20) / 20;
//                else if (px <= 100)
//                    return Math.Floor(px * 10) / 10;
//                else if (px <= 500)
//                    return Math.Floor(px * 2) / 2;
//                else if (px <= 1000)
//                    return Math.Floor(px);
//                else
//                    return Math.Floor(px / 5) * 5;

//            }
//        }
//        private static decimal ETFRegularPx(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                if (px < 50)
//                    return Math.Ceiling(px * 100) / 100;
//                else
//                    return Math.Ceiling(px * 20) / 20;
//            }
//            else
//            {
//                if (px <= 50)
//                    return Math.Floor(px * 100) / 100;
//                else
//                    return Math.Floor(px * 20) / 20;
//            }
//        }
//        private static decimal TGORegularPx(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                return Math.Ceiling(px * 2) / 2;
//            }
//            else
//            {
//                return Math.Floor(px * 2) / 2;
//            }
//        }
//        private static decimal XIORegularPx(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                if (px < 20)
//                    return Math.Ceiling(px * 5) / 5;
//                else if (px < 100)
//                    return Math.Ceiling(px);
//                else if (px < 1000)
//                    return Math.Ceiling(px / 2) * 2;
//                else if (px < 2000)
//                    return Math.Ceiling(px / 10) * 10;
//                else
//                    return Math.Ceiling(px / 20) * 20;

//            }
//            else
//            {
//                if (px <= 20)
//                    return Math.Floor(px * 5) / 5;
//                else if (px <= 100)
//                    return Math.Floor(px);
//                else if (px <= 1000)
//                    return Math.Floor(px / 2) * 2;
//                else if (px <= 2000)
//                    return Math.Floor(px / 10) * 10;
//                else
//                    return Math.Floor(px / 20) * 20;

//            }
//        }
//        private static decimal GTORegularPx(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                if (px < (decimal)0.5)
//                    return Math.Ceiling(px * 200) / 200;
//                else if (px < (decimal)2.5)
//                    return Math.Ceiling(px * 40) / 40;
//                else if (px < 25)
//                    return Math.Ceiling(px * 20) / 20;
//                else if (px < 50)
//                    return Math.Ceiling(px * 4) / 4;
//                else
//                    return Math.Ceiling(px * 2) / 2;

//            }
//            else
//            {
//                if (px <= (decimal)0.5)
//                    return Math.Floor(px * 200) / 200;
//                else if (px <= (decimal)2.5)
//                    return Math.Floor(px * 40) / 40;
//                else if (px <= 25)
//                    return Math.Floor(px * 20) / 20;
//                else if (px <= 50)
//                    return Math.Floor(px * 4) / 4;
//                else
//                    return Math.Floor(px * 2) / 2;

//            }
//        }
//        private static decimal TEORegularPx(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                if (px < (decimal)0.5)
//                    return Math.Ceiling(px * 200) / 200;
//                else if (px < (decimal)2.5)
//                    return Math.Ceiling(px * 40) / 40;
//                else if (px < 25)
//                    return Math.Ceiling(px * 20) / 20;
//                else if (px < 50)
//                    return Math.Ceiling(px * 4) / 4;
//                else
//                    return Math.Ceiling(px * 2) / 2;

//            }
//            else
//            {
//                if (px <= (decimal)0.5)
//                    return Math.Floor(px * 200) / 200;
//                else if (px <= (decimal)2.5)
//                    return Math.Floor(px * 40) / 40;
//                else if (px <= 25)
//                    return Math.Floor(px * 20) / 20;
//                else if (px <= 50)
//                    return Math.Floor(px * 4) / 4;
//                else
//                    return Math.Floor(px * 2) / 2;

//            }
//        }
//        private static decimal TFORegularPx(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                if (px < 2)
//                    return Math.Ceiling(px * 50) / 50;
//                else if (px < 10)
//                    return Math.Ceiling(px * 10) / 10;
//                else if (px < 100)
//                    return Math.Ceiling(px * 5) / 5;
//                else if (px < 200)
//                    return Math.Ceiling(px);
//                else
//                    return Math.Ceiling(px / 2) * 2;

//            }
//            else
//            {
//                if (px <= 2)
//                    return Math.Floor(px * 50) / 50;
//                else if (px <= 10)
//                    return Math.Floor(px * 10) / 10;
//                else if (px <= 100)
//                    return Math.Floor(px * 5) / 5;
//                else if (px <= 200)
//                    return Math.Floor(px);
//                else
//                    return Math.Floor(px / 2) * 2;

//            }
//        }
//        private static decimal TXORegularPx(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                if (px < 10)
//                    return Math.Ceiling(px * 10) / 10;
//                else if (px < 50)
//                    return Math.Ceiling(px * 2) / 2;
//                else if (px < 500)
//                    return Math.Ceiling(px);
//                else if (px < 1000)
//                    return Math.Ceiling(px / 5) * 5;
//                else
//                    return Math.Ceiling(px / 10) * 10;

//            }
//            else
//            {
//                if (px <= 10)
//                    return Math.Floor(px * 10) / 10;
//                else if (px <= 50)
//                    return Math.Floor(px * 2) / 2;
//                else if (px <= 500)
//                    return Math.Floor(px);
//                else if (px <= 1000)
//                    return Math.Floor(px / 5) * 5;
//                else
//                    return Math.Floor(px / 10) * 10;
//            }

//        }
//        private static decimal STORegularPx(decimal px, bool goUp)
//        {
//            decimal rv = -1;
//            if (px < 5)
//            {
//                rv = goUp ? Math.Ceiling(px * 100) / 100 : Math.Floor(px * 100) / 100;
//            }
//            else if (px < 15)
//            {
//                rv = goUp ? Math.Ceiling(px * 20) / 20 : Math.Floor(px * 20) / 20;
//            }
//            else if (px < 50)
//            {
//                rv = goUp ? Math.Ceiling(px * 10) / 10 : Math.Floor(px * 10) / 10;
//            }
//            else if (px < 150)
//            {
//                rv = goUp ? Math.Ceiling(px * 2) / 2 : Math.Floor(px * 2) / 2;
//            }
//            else if (px < 1000)
//            {
//                rv = goUp ? Math.Ceiling(px) : Math.Floor(px);
//            }
//            else
//            {
//                rv = goUp ? Math.Ceiling(px / 5) * 5 : Math.Floor(px / 5) * 5;
//            }
//            return rv;
//        }
//        private static decimal DefaultPx(TickTypeEnum tickType, decimal px, bool goUp)
//        {
//            decimal ticks = (decimal)TickSize[(int)tickType];
//            if (goUp)
//                return Math.Ceiling(px / ticks) * ticks;
//            else
//                return Math.Floor(px / ticks) * ticks;
//        }
//        //取得最大報價寬度
//        public static decimal getQutPx(string symbolID, decimal px, bool getAsk, bool isFront, bool isRegularPeriod, decimal ulyPx)
//        {
//            return getQutPx(getQutType(symbolID), px, getAsk, isFront, isRegularPeriod, ulyPx);
//        }
//        //取得最大報價寬度
//        public static decimal getQutPx(QuoteTypeEnum qutType, decimal px, bool getAsk, bool isFront, bool isRegularPeriod, decimal ulyPx)
//        {
//            decimal rv = 0;
//            switch (qutType)
//            {
//                case QuoteTypeEnum.qtTXO:
//                    rv = isFront ? QutTXOFront(px, getAsk) : QutTXONext(px, getAsk);
//                    break;
//                case QuoteTypeEnum.qtRTO:
//                    rv = isFront ? QutRTOFront(px, getAsk, isRegularPeriod) : QutRTONext(px, getAsk, isRegularPeriod);
//                    break;
//                case QuoteTypeEnum.qtRTF:
//                    rv = isFront ? QutRTFFront(px, getAsk, isRegularPeriod) : QutRTFNext(px, getAsk, isRegularPeriod);
//                    break;
//                case QuoteTypeEnum.qtXEF:
//                    rv = QutXEF(px, getAsk, isFront);
//                    break;
//                case QuoteTypeEnum.qtSPF:
//                    rv = QutSPF(px, getAsk, isFront);
//                    break;
//                case QuoteTypeEnum.qtTGF:
//                    rv = QutTGF(px, getAsk);
//                    break;
//                case QuoteTypeEnum.qtGDF:
//                    rv = QutGDF(px, getAsk);
//                    break;
//                case QuoteTypeEnum.qtI5F:
//                    rv = QutI5F(px, getAsk, isFront);
//                    break;
//                case QuoteTypeEnum.qtTJF:
//                    rv = QutTJF(px, getAsk, isFront);
//                    break;
//                case QuoteTypeEnum.qtT5F:
//                    rv = QutT5F(px, getAsk);
//                    break;
//                case QuoteTypeEnum.qtGTF:
//                    rv = QutGTF(px, getAsk);
//                    break;
//                case QuoteTypeEnum.qtXIF:
//                    rv = QutXIF(px, getAsk);
//                    break;
//                case QuoteTypeEnum.qtSTF:
//                    rv = QutSTF(px, getAsk);
//                    break;
//                case QuoteTypeEnum.qtTGO:
//                    rv = QutTGO(px, getAsk, isFront);
//                    break;
//                case QuoteTypeEnum.qtGTO:
//                    rv = QutGTO(px, getAsk, isFront);
//                    break;
//                case QuoteTypeEnum.qtXIO:
//                    rv = QutXIO(px, getAsk, isFront);
//                    break;
//                case QuoteTypeEnum.qtSTO:
//                    rv = QutSTO(px, getAsk, isFront, ulyPx);
//                    break;
//                case QuoteTypeEnum.qtTEO:
//                    rv = QutTEO(px, getAsk, isFront);
//                    break;
//                case QuoteTypeEnum.qtTFO:
//                    rv = QutTFO(px, getAsk, isFront);
//                    break;
//                case QuoteTypeEnum.qtUDF:
//                    rv = QutUDF(px, getAsk, isFront);
//                    break;
//                case QuoteTypeEnum.qtXJF:

//                    break;
//                case QuoteTypeEnum.qtGBF:

//                    break;
//            }

//            return rv;
//        }
//        //道瓊期
//        private static decimal QutUDF(decimal px, bool getAsk, bool isFront)
//        {
//            if (isFront)
//            {
//                if (getAsk)
//                    return px + 6;
//                else
//                    return px - 6;
//            }
//            else
//            {
//                if (getAsk)
//                    return px + 10;
//                else
//                    return px - 10;
//            }

//        }
//        //金融選
//        private static decimal QutTFO(decimal px, bool getAsk, bool isFront)
//        {
//            if (isFront)
//            {
//                if (getAsk)
//                {
//                    if (px < (decimal)9.6)
//                        return px + (decimal)0.5;
//                    else if (px < (decimal)10.9)
//                        return (decimal)10.2;
//                    else if (px == (decimal)10.9)
//                        return (decimal)10.4;
//                    else if (px < (decimal)20)
//                        return px + 1;
//                    else if (px < 40)
//                        return px + (decimal)1.4;
//                    else if (px < 98)
//                        return 100;
//                    else if (px < 99)
//                        return 101;
//                    else if (px < 100)
//                        return 102;
//                    else
//                        return px + 3;

//                }
//                else
//                {
//                    if (px <= (decimal)0.52)
//                        return (decimal)0.02;
//                    else if (px <= (decimal)10.5)
//                        return px - (decimal)0.5;
//                    else if (px <= (decimal)11.2)
//                        return (decimal)10.2;
//                    else if (px <= (decimal)21.2)
//                        return px - 1;
//                    else if (px <= (decimal)21.6)
//                        return (decimal)20.2;
//                    else if (px <= (decimal)41.6)
//                        return px - (decimal)1.4;
//                    else if (px <= (decimal)43.2)
//                        return (decimal)40.2;
//                    else
//                        return px - 3;
//                }
//            }
//            else
//            {
//                if (getAsk)
//                {
//                    if (px < (decimal)9.3)
//                        return px + (decimal)0.8;
//                    else if (px < (decimal)9.6)
//                        return (decimal)10.2;
//                    else if (px < (decimal)9.8)
//                        return (decimal)10.4;
//                    else if (px < 10)
//                        return (decimal)10.6;
//                    else if (px < 20)
//                        return px + (decimal)1.2;
//                    else if (px < 40)
//                        return px + (decimal)2.4;
//                    else if (px < (decimal)95.6)
//                        return px + (decimal)4.6;
//                    else if (px < (decimal)96.4)
//                        return 100;
//                    else if (px < (decimal)97.4)
//                        return 101;
//                    else if (px < (decimal)98.4)
//                        return 102;
//                    else if (px < (decimal)99.4)
//                        return 103;
//                    else if (px < 101)
//                        return 104;
//                    else
//                        return px + 4;

//                }
//                else
//                {
//                    if (px <= (decimal)0.82)
//                        return (decimal)0.02;
//                    else if (px <= 11)
//                        return px - (decimal)0.8;
//                    else if (px <= (decimal)11.4)
//                        return (decimal)10.2;
//                    else if (px <= (decimal)21.4)
//                        return px - (decimal)1.2;
//                    else if (px <= (decimal)22.6)
//                        return (decimal)20.2;
//                    else if (px <= (decimal)42.6)
//                        return px - (decimal)2.4;
//                    else if (px <= (decimal)44.8)
//                        return (decimal)40.2;
//                    else if (px <= 104)
//                        return px - (decimal)4.6;
//                    else
//                        return px - 4;
//                }
//            }
//        }
//        //電子選
//        private static decimal QutTEO(decimal px, bool getAsk, bool isFront)
//        {
//            decimal rv = px;
//            if (isFront)
//            {
//                if (getAsk)
//                {
//                    if (px < 5)
//                        rv = px + (decimal)0.2;
//                    else if (px < 10)
//                        rv = px + (decimal)0.4;
//                    else if (px < 20)
//                        rv = px + (decimal)0.8;
//                    else
//                        rv = px + (decimal)1.6;
//                    return getRegularPx("TEO", rv, !getAsk);
//                }
//                else
//                {
//                    if (px <= (decimal)0.205)
//                        return (decimal)0.005;
//                    else if (px <= (decimal)5.2)
//                        return px - (decimal)0.2;
//                    else if (px <= (decimal)5.45)
//                        return (decimal)5.05;
//                    else if (px <= (decimal)10.45)
//                        return px - (decimal)0.4;
//                    else if (px <= (decimal)10.8)
//                        return (decimal)10.05;
//                    else if (px <= (decimal)20.85)
//                        return px - (decimal)0.8;
//                    else if (px <= (decimal)21.65)
//                        return (decimal)20.05;
//                    else
//                        return px - (decimal)1.6;
//                }
//            }
//            else
//            {
//                if (getAsk)
//                {
//                    if (px < 5)
//                        return px + (decimal)0.3;
//                    else if (px < 10)
//                        return px + (decimal)0.6;
//                    else if (px < 20)
//                        return px + (decimal)1.2;
//                    else
//                        return px + (decimal)2.5;

//                }
//                else
//                {
//                    if (px <= (decimal)0.305)
//                        return (decimal)0.005;
//                    else if (px <= (decimal)5.35)
//                        return px - (decimal)0.3;
//                    else if (px <= (decimal)5.65)
//                        return (decimal)5.05;
//                    else if (px <= (decimal)10.65)
//                        return px - (decimal)0.6;
//                    else if (px <= (decimal)10.8)
//                        return (decimal)10.05;
//                    else if (px <= (decimal)20.85)
//                        return px - (decimal)0.8;
//                    else if (px <= (decimal)21.65)
//                        return (decimal)20.05;
//                    else
//                        return px - (decimal)1.6;
//                }
//            }
//        }
//        //個股選
//        private static decimal QutSTO(decimal px, bool getAsk, bool isFront, decimal ulyPx)
//        {
//            decimal rv;
//            if (isFront)
//            {
//                if (ulyPx < 50)
//                {
//                    if (getAsk)
//                        rv = Math.Max(px * (decimal)1.1, px + (decimal)0.1);
//                    else
//                        rv = Math.Max(px / (decimal)1.1, px - (decimal)0.1);
//                }
//                else if (ulyPx >= 100)
//                {
//                    if (getAsk)
//                        rv = Math.Max(px * (decimal)1.1, px + 1);
//                    else
//                        rv = Math.Max(px / (decimal)1.1, px - 1);
//                }
//                else
//                {
//                    if (getAsk)
//                        rv = Math.Max(px * (decimal)1.1, px + (decimal)0.25);
//                    else
//                        rv = Math.Max(px / (decimal)1.1, px - (decimal)0.25);
//                }
//            }
//            else
//            {
//                if (ulyPx < 50)
//                {
//                    if (getAsk)
//                        rv = Math.Max(px * (decimal)1.5, px + (decimal)0.12);
//                    else
//                        rv = Math.Max(px / (decimal)1.5, px - (decimal)0.12);
//                }
//                else if (ulyPx >= 100)
//                {
//                    if (getAsk)
//                        rv = Math.Max(px * (decimal)1.5, px + (decimal)1.2);
//                    else
//                        rv = Math.Max(px / (decimal)1.5, px - (decimal)1.2);
//                }
//                else
//                {
//                    if (getAsk)
//                        rv = Math.Max(px * (decimal)1.5, px + (decimal)0.3);
//                    else
//                        rv = Math.Max(px / (decimal)1.5, px - (decimal)0.3);
//                }
//            }

//            rv = Math.Max((decimal)0.01, rv);

//            return STORegularPx(rv, !getAsk);
//        }
//        //非金電選
//        private static decimal QutXIO(decimal px, bool getAsk, bool isFront)
//        {
//            if (isFront)
//            {
//                if (getAsk)
//                {
//                    if (px < 16)
//                        return px + 4;
//                    else if (px < 17)
//                        return 20;
//                    else if (px < 18)
//                        return 21;
//                    else if (px < 19)
//                        return 22;
//                    else if (px < 20)
//                        return 23;
//                    else if (px < 50)
//                        return px + 4;
//                    else if (px < 92)
//                        return px + 8;
//                    else if (px < 94)
//                        return 100;
//                    else if (px < 96)
//                        return 102;
//                    else if (px < 98)
//                        return 104;
//                    else if (px < 100)
//                        return 106;
//                    else if (px < 250)
//                        return px + 12;
//                    else if (px < 976)
//                        return px + 24;
//                    else if (px < 986)
//                        return 1000;
//                    else if (px < 996)
//                        return 1010;
//                    else if (px < 1010)
//                        return 1020;
//                    else
//                        return px + 20;

//                }
//                else
//                {
//                    if (px <= (decimal)4.2)
//                        return (decimal)0.2;
//                    else if (px <= 55)
//                        return px - 4;
//                    else if (px <= 59)
//                        return 51;
//                    else if (px <= 110)
//                        return px - 8;
//                    else if (px <= 114)
//                        return 102;
//                    else if (px <= 264)
//                        return px - 12;
//                    else if (px <= 276)
//                        return 252;
//                    else if (px <= 1020)
//                        return px - 24;
//                    else
//                        return px - 20;
//                }

//            }
//            else
//            {
//                if (getAsk)
//                {
//                    if (px < (decimal)12.2)
//                        return px + 8;
//                    else if (px < 50)
//                        return Math.Floor(px) + 8;
//                    else if (px < 88)
//                        return px + 12;
//                    else if (px < 90)
//                        return 100;
//                    else if (px < 92)
//                        return 102;
//                    else if (px < 94)
//                        return 104;
//                    else if (px < 96)
//                        return 106;
//                    else if (px < 98)
//                        return 108;
//                    else if (px < 100)
//                        return 110;
//                    else if (px < 250)
//                        return px + 24;
//                    else if (px < 952)
//                        return px + 48;
//                    else if (px < 962)
//                        return 1000;
//                    else if (px < 972)
//                        return 1010;
//                    else if (px < 982)
//                        return 1020;
//                    else if (px < 992)
//                        return 1030;
//                    else if (px < 1010)
//                        return 1040;
//                    else
//                        return px + 40;

//                }
//                else
//                {
//                    if (px <= (decimal)9.2)
//                        return (decimal)0.2;
//                    else if (px <= 59)
//                        return px - 8;
//                    else if (px <= 63)
//                        return 51;
//                    else if (px <= 114)
//                        return px - 12;
//                    else if (px <= 126)
//                        return 102;
//                    else if (px <= 276)
//                        return px - 24;
//                    else if (px <= 300)
//                        return 252;
//                    else if (px <= 1040)
//                        return px - 48;
//                    else
//                        return px - 40;
//                }
//            }
//        }
//        //櫃買選
//        private static decimal QutGTO(decimal px, bool getAsk, bool isFront)
//        {
//            if (isFront)
//            {
//                if (getAsk)
//                {
//                    if (px < (decimal)0.305)
//                        return px + (decimal)0.2;
//                    else if (px < (decimal)0.325)
//                        return (decimal)0.5;
//                    else if (px < (decimal)0.35)
//                        return (decimal)0.525;
//                    else if (px < (decimal)0.375)
//                        return (decimal)0.55;
//                    else if (px < (decimal)0.4)
//                        return (decimal)0.575;
//                    else if (px < (decimal)0.425)
//                        return (decimal)0.6;
//                    else if (px < (decimal)0.45)
//                        return (decimal)0.625;
//                    else if (px < (decimal)0.475)
//                        return (decimal)0.65;
//                    else if (px < (decimal)0.5)
//                        return (decimal)0.675;
//                    else if (px < (decimal)2.3)
//                        return px + (decimal)0.2;
//                    else if (px < (decimal)2.35)
//                        return (decimal)2.5;
//                    else if (px < (decimal)2.4)
//                        return (decimal)2.55;
//                    else if (px < (decimal)2.45)
//                        return (decimal)2.6;
//                    else if (px < 5)
//                        return px + (decimal)0.3;
//                    else if (px < 10)
//                        return px + (decimal)0.6;
//                    else if (px < (decimal)23.8)
//                        return px + (decimal)1.2;
//                    else if (px < (decimal)24.05)
//                        return 25;
//                    else if (px < (decimal)24.3)
//                        return (decimal)25.25;
//                    else if (px < (decimal)24.55)
//                        return (decimal)25.5;
//                    else if (px < (decimal)24.8)
//                        return (decimal)25.75;
//                    else if (px < 25)
//                        return 26;
//                    else if (px < 49)
//                        return px + 1;
//                    else if (px < (decimal)49.5)
//                        return 50;
//                    else if (px < 50)
//                        return (decimal)50.5;
//                    else
//                        return px + 1;

//                }
//                else
//                {
//                    if (px <= (decimal)0.205)
//                        return (decimal)0.005;
//                    else if (px <= (decimal)2.7)
//                        return px - (decimal)0.2;
//                    else if (px <= (decimal)2.85)
//                        return (decimal)2.55;
//                    else if (px <= (decimal)5.35)
//                        return px - (decimal)0.3;
//                    else if (px <= (decimal)5.65)
//                        return (decimal)5.05;
//                    else if (px <= (decimal)10.65)
//                        return px - (decimal)0.6;
//                    else if (px <= (decimal)11.25)
//                        return (decimal)10.05;
//                    else if (px <= 26)
//                        return px - (decimal)1.2;
//                    else
//                        return px - 1;


//                }
//            }
//            else
//            {
//                if (getAsk)
//                {
//                    if (px < (decimal)0.105)
//                        return px + (decimal)0.4;
//                    else if (px < (decimal)0.125)
//                        return (decimal)0.5;
//                    else if (px < (decimal)0.15)
//                        return (decimal)0.525;
//                    else if (px < (decimal)0.175)
//                        return (decimal)0.55;
//                    else if (px < (decimal)0.2)
//                        return (decimal)0.575;
//                    else if (px < (decimal)0.225)
//                        return (decimal)0.6;
//                    else if (px < (decimal)0.25)
//                        return (decimal)0.625;
//                    else if (px < (decimal)0.275)
//                        return (decimal)0.65;
//                    else if (px < (decimal)0.3)
//                        return (decimal)0.675;
//                    else if (px < (decimal)0.325)
//                        return (decimal)0.7;
//                    else if (px < (decimal)0.375)
//                        return (decimal)0.75;
//                    else if (px < (decimal)0.4)
//                        return (decimal)0.775;
//                    else if (px < (decimal)0.425)
//                        return (decimal)0.8;
//                    else if (px < (decimal)0.45)
//                        return (decimal)0.825;
//                    else if (px < (decimal)0.475)
//                        return (decimal)0.85;
//                    else if (px < (decimal)0.5)
//                        return (decimal)0.875;

//                    else if (px < (decimal)2.1)
//                        return px + (decimal)0.4;

//                    else if (px < (decimal)2.15)
//                        return (decimal)2.5;
//                    else if (px < (decimal)2.2)
//                        return (decimal)2.55;
//                    else if (px < (decimal)2.25)
//                        return (decimal)2.6;
//                    else if (px < (decimal)2.3)
//                        return (decimal)2.65;
//                    else if (px < (decimal)2.35)
//                        return (decimal)2.7;
//                    else if (px < (decimal)2.4)
//                        return (decimal)2.75;
//                    else if (px < (decimal)2.45)
//                        return (decimal)2.8;
//                    else if (px < (decimal)2.5)
//                        return (decimal)2.85;

//                    else if (px < 5)
//                        return px + (decimal)0.6;

//                    else if (px < 10)
//                        return px + (decimal)1.2;

//                    else if (px < (decimal)22.6)
//                        return px + (decimal)2.4;
//                    else if (px < (decimal)22.85)
//                        return 25;
//                    else if (px < (decimal)23.1)
//                        return (decimal)25.25;
//                    else if (px < (decimal)23.35)
//                        return (decimal)25.5;
//                    else if (px < (decimal)23.6)
//                        return (decimal)25.75;
//                    else if (px < (decimal)23.85)
//                        return 26;
//                    else if (px < (decimal)24.1)
//                        return (decimal)26.25;
//                    else if (px < (decimal)24.35)
//                        return (decimal)26.5;
//                    else if (px < (decimal)24.6)
//                        return (decimal)26.75;
//                    else if (px < (decimal)25.25)
//                        return (decimal)27.25;

//                    else if (px < (decimal)47.75)
//                        return px + (decimal)2.25;

//                    else if (px < (decimal)48.25)
//                        return 50;
//                    else if (px < (decimal)48.75)
//                        return (decimal)50.5;
//                    else if (px < (decimal)49.25)
//                        return 51;
//                    else if (px < (decimal)49.75)
//                        return (decimal)51.5;
//                    else if (px < (decimal)50.5)
//                        return 52;
//                    else
//                        return px + 2;
//                }
//                else
//                {
//                    if (px <= (decimal)0.405)
//                        return (decimal)0.005;
//                    else if (px <= (decimal)2.95)
//                        return px - (decimal)0.4;
//                    else if (px <= (decimal)3.15)
//                        return (decimal)2.55;
//                    else if (px <= (decimal)5.65)
//                        return px - (decimal)0.6;
//                    else if (px <= (decimal)6.25)
//                        return (decimal)5.05;
//                    else if (px <= (decimal)11.3)
//                        return px - (decimal)1.2;
//                    else if (px <= (decimal)12.45)
//                        return (decimal)10.05;
//                    else if (px <= (decimal)27.25)
//                        return px - (decimal)2.4;
//                    else
//                        return px - 2;

//                }

//            }
//        }
//        //黃金選
//        private static decimal QutTGO(decimal px, bool getAsk, bool isFront)
//        {
//            if (isFront)
//            {
//                if (getAsk)
//                {
//                    if (px < 50)
//                        return px + 3;
//                    else if (px < 100)
//                        return px + 5;
//                    else if (px < 250)
//                        return px + 8;
//                    else
//                        return px + 15;
//                }
//                else
//                {
//                    if (px <= (decimal)3.5)
//                        return (decimal)0.5;
//                    else if (px <= (decimal)53.5)
//                        return px - 3;
//                    else if (px <= (decimal)55.5)
//                        return (decimal)50.5;
//                    else if (px <= (decimal)105.5)
//                        return px - 5;
//                    else if (px <= (decimal)108.5)
//                        return (decimal)100.5;
//                    else if (px < (decimal)258.5)
//                        return px - 8;
//                    else if (px < (decimal)265.5)
//                        return (decimal)250.5;
//                    else
//                        return px - 15;
//                }
//            }
//            else
//            {
//                if (getAsk)
//                {
//                    if (px < 50)
//                        return px + 5;
//                    else if (px < 100)
//                        return px + 8;
//                    else if (px < 250)
//                        return px + 15;
//                    else
//                        return px + 25;
//                }
//                else
//                {
//                    if (px <= (decimal)5.5)
//                        return (decimal)0.5;
//                    else if (px <= (decimal)55.5)
//                        return px - 5;
//                    else if (px <= (decimal)58.5)
//                        return (decimal)50.5;
//                    else if (px <= (decimal)108.5)
//                        return px - 8;
//                    else if (px <= (decimal)115.5)
//                        return (decimal)100.5;
//                    else if (px < (decimal)265.5)
//                        return px - 15;
//                    else if (px < (decimal)275.5)
//                        return (decimal)250.5;
//                    else
//                        return px - 25;
//                }
//            }
//        }
//        //個股期
//        private static decimal QutSTF(decimal px, bool getAsk)
//        {
//            decimal next = px;
//            while (true)
//            {
//                if (Math.Abs(next - px) / (getAsk ? px : next) > (decimal)0.01)
//                    break;
//                else
//                    next = getNextTick(next, "STF", getAsk);
//            }
//            return getNextTick(next, "STF", !getAsk);
//        }
//        //非金電期
//        private static decimal QutXIF(decimal px, bool getAsk)
//        {
//            if (getAsk)
//                return px + 15;
//            else
//                return px - 15;
//        }
//        //櫃買期
//        private static decimal QutGTF(decimal px, bool getAsk)
//        {
//            if (getAsk)
//                return px + 10;
//            else
//                return px - 10;
//        }
//        //台灣50期
//        private static decimal QutT5F(decimal px, bool getAsk)
//        {
//            if (getAsk)
//                return px + 15;
//            else
//                return px - 15;
//        }
//        //東證期
//        private static decimal QutTJF(decimal px, bool getAsk, bool isFront)
//        {
//            if (getAsk)
//                return px + (isFront ? 4 : 6);
//            else
//                return px - (isFront ? 4 : 6);
//        }
//        //印度期
//        private static decimal QutI5F(decimal px, bool getAsk, bool isFront)
//        {
//            if (getAsk)
//                return px + (isFront ? 5 : 7);
//            else
//                return px - (isFront ? 5 : 7);
//        }
//        //美元黃金期
//        private static decimal QutGDF(decimal px, bool getAsk)
//        {
//            if (getAsk)
//                return px + 12 * (decimal)0.1;
//            else
//                return px - 12 * (decimal)0.1;
//        }
//        //台幣黃金期
//        private static decimal QutTGF(decimal px, bool getAsk)
//        {
//            if (getAsk)
//                return px + 12 * (decimal)0.5;
//            else
//                return px - 12 * (decimal)0.5;
//        }
//        //S&P期貨
//        private static decimal QutSPF(decimal px, bool getAsk, bool isFront)
//        {
//            if (isFront)
//            {
//                if (getAsk)
//                    return px + 6 * (decimal)0.25;
//                else
//                    return px - 6 * (decimal)0.25;
//            }
//            else
//            {
//                if (getAsk)
//                    return px + 10 * (decimal)0.25;
//                else
//                    return px - 10 * (decimal)0.25;
//            }

//        }
//        //歐元期貨
//        private static decimal QutXEF(decimal px, bool getAsk, bool isFront)
//        {
//            if (getAsk)
//                return px + (isFront ? 6 : 10) * (decimal)0.0001;
//            else
//                return px - (isFront ? 6 : 10) * (decimal)0.0001;
//        }
//        //其他季月 人民幣期
//        private static decimal QutRTFNext(decimal px, bool getAsk, bool isRegularPeriod)
//        {
//            if (isRegularPeriod)
//            {
//                if (getAsk)
//                    return px + 40 * (decimal)0.0001;
//                else
//                    return px - 40 * (decimal)0.0001;
//            }
//            else
//            {
//                if (getAsk)
//                    return px + 100 * (decimal)0.0001;
//                else
//                    return px - 100 * (decimal)0.0001;
//            }
//        }
//        //近三月 人民幣期
//        private static decimal QutRTFFront(decimal px, bool getAsk, bool isRegularPeriod)
//        {
//            if (isRegularPeriod)
//            {
//                if (getAsk)
//                    return px + 30 * (decimal)0.0001;
//                else
//                    return px - 30 * (decimal)0.0001;
//            }
//            else
//            {
//                if (getAsk)
//                    return px + 60 * (decimal)0.0001;
//                else
//                    return px - 60 * (decimal)0.0001;
//            }
//        }
//        //TXO 中價
//        public static  void GetTXOQut(double midPx, out double buyQutPx, out double sellQutPx)
//        {
//            decimal upPx = TXORegularPx((decimal)midPx,true);
//            decimal dnPx = TXORegularPx((decimal)midPx, false);
//            if(Math.Abs(upPx - (decimal)midPx) < Math.Abs(dnPx - (decimal)midPx))
//            {
//                QutMidTXO_Up(midPx,out buyQutPx,out sellQutPx);
//            }
//            else
//            {
//                QutMidTXO_Down(midPx, out buyQutPx, out sellQutPx);
//            }
//        }
//        public static void QutMidTXO_Up(double midPx, out double buyQutPx, out double sellQutPx)
//        {
//            buyQutPx = 0.1;
//            sellQutPx = 5000;

//            if (midPx <= 0.1)
//            {
//                buyQutPx = 0;
//                sellQutPx = 0.6;
//            }
//            else if (midPx > 0.1 && midPx <= 1.1)
//            {
//                buyQutPx = 0.0;
//                sellQutPx = 2.1;
//            }
//            else if (midPx > 1.1 && midPx <= 9.0)
//            {
//                buyQutPx = midPx - 1;
//                sellQutPx = midPx + 1;
//            }
//            else if (midPx > 9 && midPx <= 9.2)
//            {
//                buyQutPx = 8.0;
//                sellQutPx = 10.0;
//            }
//            else if (midPx > 9.2 && midPx <= 9.7)
//            {
//                buyQutPx = 8.5;
//                sellQutPx = 10.5;
//            }
//            else if (midPx > 9.7 && midPx <= 10)
//            {
//                buyQutPx = 9;
//                sellQutPx = 11;
//            }
//            else if (midPx > 10 && midPx <= 15.5)
//            {
//                buyQutPx = midPx - 1;
//                sellQutPx = midPx + 1;
//            }
//            else if (midPx > 15.5 && midPx <= 16.5)
//            {
//                buyQutPx = 15;
//                sellQutPx = 18;
//            }
//            else if (midPx > 16.5 && midPx <= 48.5)
//            {
//                buyQutPx = midPx - 1.5;
//                sellQutPx = midPx + 1.5;
//            }
//            else if (midPx > 11.5 && midPx <= 48.5)
//            {
//                buyQutPx = midPx - 1.5;
//                sellQutPx = midPx + 1.5;
//            }
//            else if (midPx > 48.5 && midPx <= 49.5)
//            {
//                buyQutPx = 48;
//                sellQutPx = 51;
//            }
//            else if (midPx > 49.5 && midPx <= 50)
//            {
//                buyQutPx = 49;
//                sellQutPx = 52;
//            }
//            else if (midPx > 50 && midPx <= 52)
//            {
//                buyQutPx = 50;
//                sellQutPx = 55;
//            }
//            else if (midPx > 52 && midPx <= 101)
//            {
//                buyQutPx = midPx - 2;
//                sellQutPx = midPx + 3;
//            }
//            else if (midPx > 101 && midPx <= 104)
//            {
//                buyQutPx = 100;
//                sellQutPx = 108;
//            }
//            else if (midPx > 104 && midPx <= 253)
//            {
//                buyQutPx = midPx - 4;
//                sellQutPx = midPx + 4;
//            }
//            else if (midPx > 254 && midPx <= 255)
//            {
//                buyQutPx = 250;
//                sellQutPx = 260;
//            }
//            else if (midPx > 255 && midPx <= 495)
//            {
//                buyQutPx = midPx - 5;
//                sellQutPx = midPx + 5;
//            }
//            else if (midPx > 495 && midPx <= 496)
//            {
//                buyQutPx = 490;
//                sellQutPx = 500;
//            }
//            else if (midPx > 496 && midPx <= 500)
//            {
//                buyQutPx = 495;
//                sellQutPx = 505;
//            }
//            else if (midPx > 500 && midPx <= 990)
//            {
//                buyQutPx = midPx - 5;
//                sellQutPx = midPx + 10;
//            }
//            else if (midPx > 990 && midPx <= 995)
//            {
//                buyQutPx = 985;
//                sellQutPx = 1000;
//            }
//            else if (midPx > 995 && midPx <= 1000)
//            {
//                buyQutPx = 995;
//                sellQutPx = 1010;
//            }
//            else if (midPx > 1000)
//            {
//                buyQutPx = midPx - 10;
//                sellQutPx = midPx + 10;
//            }
//        }
//        public static void QutMidTXO_Down(double midPx, out double buyQutPx, out double sellQutPx)
//        {
//            buyQutPx = 0.1;
//            sellQutPx = 5000;

//            if (midPx <= 0.1)
//            {
//                buyQutPx = 0;
//                sellQutPx = 0.6;
//            }
//            else if (midPx > 0.1 && midPx <= 1.1)
//            {
//                buyQutPx = 0.0;
//                sellQutPx = 2.1;
//            }
//            else if (midPx > 1.1 && midPx <= 9.0)
//            {
//                buyQutPx = midPx - 1;
//                sellQutPx = midPx + 1;
//            }
//            else if (midPx > 9 && midPx <= 9.2)
//            {
//                buyQutPx = 8.0;
//                sellQutPx = 10.0;
//            }
//            else if (midPx > 9.2 && midPx <= 9.7)
//            {
//                buyQutPx = 8.5;
//                sellQutPx = 10.5;
//            }
//            else if (midPx > 9.7 && midPx <= 10)
//            {
//                buyQutPx = 9;
//                sellQutPx = 11;
//            }
//            else if (midPx > 10 && midPx <= 15.5)
//            {
//                buyQutPx = midPx - 1;
//                sellQutPx = midPx + 1;
//            }
//            else if (midPx > 15.5 && midPx <= 16.5)
//            {
//                buyQutPx = 15;
//                sellQutPx = 18;
//            }
//            else if (midPx > 16.5 && midPx <= 48.5)
//            {
//                buyQutPx = midPx - 1.5;
//                sellQutPx = midPx + 1.5;
//            }
//            else if (midPx > 11.5 && midPx <= 48.5)
//            {
//                buyQutPx = midPx - 1.5;
//                sellQutPx = midPx + 1.5;
//            }
//            else if (midPx > 48.5 && midPx <= 49.5)
//            {
//                buyQutPx = 48;
//                sellQutPx = 51;
//            }
//            else if (midPx > 49.5 && midPx <= 51)
//            {
//                buyQutPx = 49;
//                sellQutPx = 52;
//            }
//            else if (midPx > 51 && midPx <= 53)
//            {
//                buyQutPx = 50;
//                sellQutPx = 55;
//            }
//            else if (midPx > 53 && midPx <= 102)
//            {
//                buyQutPx = midPx - 3;
//                sellQutPx = midPx + 2;
//            }
//            else if (midPx > 102 && midPx <= 104)
//            {
//                buyQutPx = 100;
//                sellQutPx = 108;
//            }
//            else if (midPx > 104 && midPx <= 253)
//            {
//                buyQutPx = midPx - 4;
//                sellQutPx = midPx + 4;
//            }
//            else if (midPx > 254 && midPx <= 255)
//            {
//                buyQutPx = 250;
//                sellQutPx = 260;
//            }
//            else if (midPx > 255 && midPx <= 495)
//            {
//                buyQutPx = midPx - 5;
//                sellQutPx = midPx + 5;
//            }
//            else if (midPx > 495 && midPx <= 497)
//            {
//                buyQutPx = 490;
//                sellQutPx = 500;
//            }
//            else if (midPx > 497 && midPx <= 500)
//            {
//                buyQutPx = 495;
//                sellQutPx = 505;
//            }
//            else if (midPx > 500 && midPx <= 995)
//            {
//                buyQutPx = midPx - 10;
//                sellQutPx = midPx + 5;
//            }
//            else if (midPx > 995 && midPx <= 1000)
//            {
//                buyQutPx = 995;
//                sellQutPx = 1010;
//            }
//            else if (midPx > 1000)
//            {
//                buyQutPx = midPx - 10;
//                sellQutPx = midPx + 10;
//            }
//        }

//        //近三月 人民幣選
//        public static decimal QutRTOFront(decimal px, bool getAsk, bool isRegularPeriod)
//        {
//            if (isRegularPeriod)
//            {
//                decimal rv = px * (decimal)0.3;
//                rv = Math.Max(rv, (decimal)0.005);
//                rv = Math.Floor(rv * 10000) * 10000;
//                if (getAsk)
//                    return px + rv;
//                else
//                    return px - rv;

//            }
//            else
//            {
//                decimal rv = px * (decimal)0.45;
//                rv = Math.Max(rv, (decimal)0.0075);
//                rv = Math.Floor(rv * 10000) * 10000;
//                if (getAsk)
//                    return px + rv;
//                else
//                    return px - rv;

//            }

//        }
//        //其他季月 人民幣選
//        public static decimal QutRTONext(decimal px, bool getAsk, bool isRegular)
//        {
//            if (isRegular)
//            {
//                decimal rv = px * (decimal)0.1;
//                rv = Math.Max(rv, (decimal)0.005);
//                rv = Math.Floor(rv * 10000) * 10000;
//                if (getAsk)
//                    return px + rv;
//                else
//                    return px - rv;

//            }
//            else
//            {
//                decimal rv = px * (decimal)0.2;
//                rv = Math.Max(rv, (decimal)0.01);
//                rv = Math.Floor(rv * 10000) * 10000;
//                if (getAsk)
//                    return px + rv;
//                else
//                    return px - rv;

//            }
//        }
//        //近月TXO
//        public static decimal QutTXOFront(decimal px, bool getAsk)
//        {
//            if (getAsk)
//            {
//                if (px < 8)
//                    return px + 2;
//                else if (px < (decimal)8.6)
//                    return 10;
//                else if (px < (decimal)9.1)
//                    return (decimal)10.5;
//                else if (px < (decimal)9.6)
//                    return (decimal)11;
//                else if (px < 10)
//                    return (decimal)11.5;
//                else if (px < 15)
//                    return px + 2;
//                else if (px < 47)
//                    return px + 3;
//                else if (px < 48)
//                    return 50;
//                else if (px < 49)
//                    return 51;
//                else if (px < 50)
//                    return 52;
//                else if (px < 100)
//                    return px + 5;
//                else if (px < 250)
//                    return px + 8;
//                else if (px < 491)
//                    return px + 10;
//                else if (px < 495)
//                    return 500;
//                else if (px < 500)
//                    return 505;
//                else if (px < 1000)
//                    return px + 10;
//                else
//                    return px + 20;

//            }
//            else
//            {
//                if (px <= (decimal)2.1)
//                    return (decimal)0.1;
//                else if (px <= (decimal)17.5)
//                    return px - 2;
//                else if (px == 18)
//                    return (decimal)15.5;
//                else if (px <= 54)
//                    return px - 3;
//                else if (px == 55)
//                    return 50;
//                else if (px <= 105)
//                    return px - 5;
//                else if (px <= 109)
//                    return 100;
//                else if (px <= 259)
//                    return px - 8;
//                else if (px <= 261)
//                    return 250;
//                else if (px <= 1020)
//                    return px - 10;
//                else if (px == 1030)
//                    return 1000;
//                else
//                    return px - 20;
//            }

//        }
//        //次近月TXO
//        public static decimal QutTXONext(decimal px, bool getAsk)
//        {
//            if (getAsk)
//            {
//                if (px < 15)
//                    return px + 4;
//                else if (px < 50)
//                    return px + 5;
//                else if (px < 100)
//                    return px + 8;
//                else if (px < 250)
//                    return px + 15;
//                else if (px < 1000)
//                    return px + 20;
//                else
//                    return px + 40;

//            }
//            else
//            {
//                if (px <= (decimal)4.1)
//                    return (decimal)0.1;
//                else if (px <= (decimal)19.5)
//                    return px - 4;
//                else if (px == 20)
//                    return (decimal)15.5;
//                else if (px <= 56)
//                    return px - 5;
//                else if (px <= 58)
//                    return 50;
//                else if (px <= 109)
//                    return px - 8;
//                else if (px <= 116)
//                    return 100;
//                else if (px <= 265)
//                    return px - 15;
//                else if (px <= 271)
//                    return 250;
//                else if (px <= 1030)
//                    return px - 20;
//                else if (px == 1050)
//                    return 1000;
//                else
//                    return px - 40;
//            }

//        }
//        //次2近月TXO
//        public static decimal QutTXONext2(decimal px, bool getAsk)
//        {
//            if (getAsk)
//            {
//                if (px < 15)
//                    return px + 6;
//                else if (px < 50)
//                    return px + 8;
//                else if (px < 100)
//                    return px + 12;
//                else if (px < 250)
//                    return px + 20;
//                else if (px < 1000)
//                    return px + 30;
//                else
//                    return px + 40;

//            }
//            else
//            {
//                if (px <= (decimal)6.1)
//                    return (decimal)0.1;
//                else if (px <= (decimal)21.5)
//                    return px - 6;
//                else if (px == 23)
//                    return (decimal)15.5;
//                else if (px <= 58)
//                    return px - 8;
//                else if (px <= 62)
//                    return 50;
//                else if (px <= 112)
//                    return px - 12;
//                else if (px <= 120)
//                    return 100;
//                else if (px <= 275)
//                    return px - 20;
//                else if (px <= 281)
//                    return 250;
//                else if (px <= 1030)
//                    return px - 30;
//                else if (px == 1040)
//                    return 1000;
//                else
//                    return px - 40;
//            }

//        }
//        //TXO季月
//        public static decimal QutTXOQuarter(decimal px, bool getAsk)
//        {
//            if (getAsk)
//            {
//                if (px < 15)
//                    return px + 8;
//                else if (px < 50)
//                    return px + 12;
//                else if (px < 100)
//                    return px + 20;
//                else if (px < 250)
//                    return px + 30;
//                else
//                    return px + 40;

//            }
//            else
//            {
//                if (px <= (decimal)8.1)
//                    return (decimal)0.1;
//                else if (px <= (decimal)23.5)
//                    return px - 8;
//                else if (px <= (decimal)27.5)
//                    return (decimal)15.5;
//                else if (px <= 63)
//                    return px - 12;
//                else if (px <= 71)
//                    return 50;
//                else if (px <= 121)
//                    return px - 20;
//                else if (px <= 131)
//                    return 100;
//                else if (px <= 281)
//                    return px - 30;
//                else if (px <= 291)
//                    return 250;
//                else
//                    return px - 40;
//            }
//        }
//       //取得價格序列
//        public static List<decimal> getPrcList(TickTypeEnum tickType, decimal UpLimit, decimal DnLimit)
//        {
//            List<decimal> rv = new List<decimal>();

//            decimal next = UpLimit;
//            while (next >= DnLimit)
//            {
//                rv.Add(next);
//                next = getNextTick(next, tickType, false);
//            }
//            return rv;
//        }
//        //取得價格序列
//        public static List<double> getPrcListDouble(TickTypeEnum tickType, double UpLimit, double DnLimit)
//        {
//            List<double> rv = new List<double>();

//            double next = UpLimit;
//            while (next >= DnLimit)
//            {
//                rv.Add(next);
//                next = (double)getNextTick((decimal)next, tickType, false);
//            }
//            return rv;
//        }
//        //取得小數位數
//        public static int getPrcDigital(string symbolID)
//        {
//            int tt = 0;
//            switch (symbolID)
//            {
//                case "G2F":
//                    tt = 0;
//                    break;
//                case "SSI":
//                    tt = 1;
//                    break;
//                case "STW":
//                    tt = 1;
//                    break;
//                case "1GC":
//                    tt = 1;
//                    break;
//                case "TXW":
//                    tt = 1;
//                    break;
//                case "MXW":
//                    tt = 0;
//                    break;
//                case "TXO":
//                    tt = 1;
//                    break;
//                case "STF":
//                    tt = 2;
//                    break;
//                case "STO":
//                    tt = 2;
//                    break;
//                case "ETF":
//                    tt = 2;
//                    break;
//                case "TEO":
//                    tt = 3;
//                    break;
//                case "GTO":
//                    tt = 3;
//                    break;
//                case "TFO":
//                    tt = 2;
//                    break;
//                case "XIO":
//                    tt = 1;
//                    break;
//                case "TXF":
//                    tt = 0;
//                    break;
//                case "T5F":
//                    tt = 0;
//                    break;
//                case "XIF":
//                    tt = 0;
//                    break;
//                case "I5F":
//                    tt = 0;
//                    break;
//                case "UDF":
//                    tt = 0;
//                    break;
//                case "TGF":
//                    tt = 1;
//                    break;
//                case "TGO":
//                    tt = 1;
//                    break;
//                case "TJF":
//                    tt = 2;
//                    break;
//                case "SPF":
//                    tt = 2;
//                    break;
//                case "FXF":
//                    tt = 1;
//                    break;
//                case "GDF":
//                    tt = 1;
//                    break;
//                case "EXF":
//                    tt = 2;
//                    break;
//                case "GTF":
//                    tt = 2;
//                    break;
//                case "XJF":
//                    tt = 2;
//                    break;
//                case "GBF":
//                    tt = 3;
//                    break;
//                case "RTF":
//                    tt = 4;
//                    break;
//                case "RHF":
//                    tt = 4;
//                    break;
//                case "XEF":
//                    tt = 3;
//                    break;
//                case "RTO":
//                    tt = 3;
//                    break;
//                case "RHO":
//                    tt = 3;
//                    break;
//                default:
//                    tt = 0;
//                    break;
//            }
//            return tt;

//        }
//        //取得價格序列
//        public static List<decimal> getPrcList(string symbolID, decimal UpLimit, decimal DnLimit)
//        {
//            return getPrcList(getTickType(symbolID), UpLimit, DnLimit);
//        }
//        //取得下一個TICK
//        public static decimal getNextTick(decimal px, string symbolID, bool goUp)
//        {
//            return getNextTick(px, getTickType(symbolID), goUp);
//        }
//        //商品轉換
//        public static TickTypeEnum getTickType(string symbolID)
//        {
//            TickTypeEnum tt = new TickTypeEnum();
//            switch (symbolID)
//            {
//                case "G2F":
//                    tt = TickTypeEnum.tt1;
//                    break;
//                case "SSI":
//                    tt = TickTypeEnum.tt50;
//                    break;
//                case "STW":
//                    tt = TickTypeEnum.tt01;
//                    break;
//                case "1GC":
//                    tt = TickTypeEnum.tt01;
//                    break;
//                case "TXW":
//                    tt = TickTypeEnum.ttTXO;
//                    break;
//                case "MXW":
//                    tt = TickTypeEnum.tt1;
//                    break;
//                case "MX1":
//                    tt = TickTypeEnum.tt1;
//                    break;
//                case "MX2":
//                    tt = TickTypeEnum.tt1;
//                    break;
//                case "MX3":
//                    tt = TickTypeEnum.tt1;
//                    break;
//                case "MX4":
//                    tt = TickTypeEnum.tt1;
//                    break;
//                case "MX5":
//                    tt = TickTypeEnum.tt1;
//                    break;
//                case "TX1":
//                    tt = TickTypeEnum.ttTXO;
//                    break;
//                case "TX2":
//                    tt = TickTypeEnum.ttTXO;
//                    break;
//                case "TX3":
//                    tt = TickTypeEnum.ttTXO;
//                    break;
//                case "TX4":
//                    tt = TickTypeEnum.ttTXO;
//                    break;
//                case "TX5":
//                    tt = TickTypeEnum.ttTXO;
//                    break;
//                case "TXO":
//                    tt = TickTypeEnum.ttTXO;
//                    break;
//                case "STF":
//                    tt = TickTypeEnum.ttSTF;
//                    break;
//                case "STO":
//                    tt = TickTypeEnum.ttSTO;
//                    break;
//                case "ETF":
//                    tt = TickTypeEnum.ttETF;
//                    break;
//                case "TEO":
//                    tt = TickTypeEnum.ttTEO;
//                    break;
//                case "GTO":
//                    tt = TickTypeEnum.ttTEO;
//                    break;
//                case "TFO":
//                    tt = TickTypeEnum.ttTFO;
//                    break;
//                case "XIO":
//                    tt = TickTypeEnum.ttXIO;
//                    break;
//                case "TXF":
//                    tt = TickTypeEnum.tt1;
//                    break;
//                case "T5F":
//                    tt = TickTypeEnum.tt1;
//                    break;
//                case "XIF":
//                    tt = TickTypeEnum.tt1;
//                    break;
//                case "I5F":
//                    tt = TickTypeEnum.tt1;
//                    break;
//                case "UDF":
//                    tt = TickTypeEnum.tt1;
//                    break;
//                case "TGF":
//                    tt = TickTypeEnum.tt05;
//                    break;
//                case "TGO":
//                    tt = TickTypeEnum.tt05;
//                    break;
//                case "TJF":
//                    tt = TickTypeEnum.tt025;
//                    break;
//                case "SPF":
//                    tt = TickTypeEnum.tt025;
//                    break;
//                case "FXF":
//                    tt = TickTypeEnum.tt02;
//                    break;
//                case "GDF":
//                    tt = TickTypeEnum.tt01;
//                    break;
//                case "EXF":
//                    tt = TickTypeEnum.tt005;
//                    break;
//                case "GTF":
//                    tt = TickTypeEnum.tt005;
//                    break;
//                case "XJF":
//                    tt = TickTypeEnum.tt001;
//                    break;
//                case "GBF":
//                    tt = TickTypeEnum.tt0005;
//                    break;
//                case "RTF":
//                    tt = TickTypeEnum.tt00001;
//                    break;
//                case "RHF":
//                    tt = TickTypeEnum.tt00001;
//                    break;
//                case "XEF":
//                    tt = TickTypeEnum.tt0005;
//                    break;
//                case "RTO":
//                    tt = TickTypeEnum.tt0005;
//                    break;
//                case "RHO":
//                    tt = TickTypeEnum.tt0005;
//                    break;
//                default:
//                    tt = TickTypeEnum.tt1;
//                    break;
//            }
//            return tt;
//        }
//        //商品轉換
//        public static QuoteTypeEnum getQutType(string symbolID)
//        {
//            QuoteTypeEnum qt = new QuoteTypeEnum();
//            switch (symbolID)
//            {
//                case "TXO":
//                case "TX1":
//                case "TX2":
//                case "TX3":
//                case "TX4":
//                case "TX5":
//                case "TXW":
//                    qt = QuoteTypeEnum.qtTXO;
//                    break;
//                case "STF":
//                    qt = QuoteTypeEnum.qtSTF;
//                    break;
//                case "STO":
//                    qt = QuoteTypeEnum.qtSTO;
//                    break;
//                case "TEO":
//                    qt = QuoteTypeEnum.qtTEO;
//                    break;
//                case "GTO":
//                    qt = QuoteTypeEnum.qtGTO;
//                    break;
//                case "TFO":
//                    qt = QuoteTypeEnum.qtTFO;
//                    break;
//                case "XIO":
//                    qt = QuoteTypeEnum.qtXIO;
//                    break;
//                case "T5F":
//                    qt = QuoteTypeEnum.qtT5F;
//                    break;
//                case "XIF":
//                    qt = QuoteTypeEnum.qtXIF;
//                    break;
//                case "I5F":
//                    qt = QuoteTypeEnum.qtI5F;
//                    break;
//                case "UDF":
//                    qt = QuoteTypeEnum.qtUDF;
//                    break;
//                case "TGF":
//                    qt = QuoteTypeEnum.qtTGF;
//                    break;
//                case "TGO":
//                    qt = QuoteTypeEnum.qtTGO;
//                    break;
//                case "TJF":
//                    qt = QuoteTypeEnum.qtTJF;
//                    break;
//                case "SPF":
//                    qt = QuoteTypeEnum.qtSPF;
//                    break;
//                case "GDF":
//                    qt = QuoteTypeEnum.qtGDF;
//                    break;
//                case "GTF":
//                    qt = QuoteTypeEnum.qtGTF;
//                    break;
//                case "XJF":
//                    qt = QuoteTypeEnum.qtXJF;
//                    break;
//                case "GBF":
//                    qt = QuoteTypeEnum.qtGBF;
//                    break;
//                case "RTF":
//                    qt = QuoteTypeEnum.qtRTF;
//                    break;
//                case "RHF":
//                    qt = QuoteTypeEnum.qtRTF;
//                    break;
//                case "XEF":
//                    qt = QuoteTypeEnum.qtXEF;
//                    break;
//                case "RTO":
//                    qt = QuoteTypeEnum.qtRTO;
//                    break;
//                case "RHO":
//                    qt = QuoteTypeEnum.qtRTO;
//                    break;
//                default:
//                    qt = QuoteTypeEnum.qtTXO;
//                    break;
//            }
//            return qt;
//        }

//        //取得下一個TICK
//        public static decimal getNextTick(decimal px, TickTypeEnum tickType, bool goUp)
//        {
//            decimal rv = 0;
//            switch (tickType)
//            {
//                case TickTypeEnum.ttTXO:
//                    rv = NextTXO(px, goUp);
//                    break;
//                case TickTypeEnum.ttSTF:
//                    rv = NextSTF(px, goUp);
//                    break;
//                case TickTypeEnum.ttETF:
//                    rv = NextETF(px, goUp);
//                    break;
//                case TickTypeEnum.ttSTO:
//                    rv = NextSTO(px, goUp);
//                    break;
//                case TickTypeEnum.ttTEO:
//                    rv = NextTEO(px, goUp);
//                    break;
//                case TickTypeEnum.ttTFO:
//                    rv = NextTFO(px, goUp);
//                    break;
//                case TickTypeEnum.ttXIO:
//                    rv = NextXIO(px, goUp);
//                    break;
//                default:
//                    rv = NextTick(px, tickType, goUp);
//                    break;
//            }
//            return rv;
//        }
//        //等距TICK
//        private static decimal NextTick(decimal px, TickTypeEnum tickType, bool goUp)
//        {
//            if (goUp)
//                px += (decimal)TickSize[(int)tickType];
//            else
//                px -= (decimal)TickSize[(int)tickType];

//            return px;
//        }
//        //非等距TICK
//        private static decimal NextTXO(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                if (px < 10)
//                    return px + (decimal)0.1;
//                else if (px < 50)
//                    return px + (decimal)0.5;
//                else if (px < 500)
//                    return px + 1;
//                else if (px < 1000)
//                    return px + 5;
//                else
//                    return px + 10;

//            }
//            else
//            {
//                if (px <= 10)
//                    return px - (decimal)0.1;
//                else if (px <= 50)
//                    return px - (decimal)0.5;
//                else if (px <= 500)
//                    return px - 1;
//                else if (px <= 1000)
//                    return px - 5;
//                else
//                    return px - 10;
//            }
//        }
//        private static decimal NextSTF(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                if (px < 10)
//                    return px + (decimal)0.01;
//                else if (px < 50)
//                    return px + (decimal)0.05;
//                else if (px < 100)
//                    return px + (decimal)0.1;
//                else if (px < 500)
//                    return px + (decimal)0.5;
//                else if (px < 1000)
//                    return px + 1;
//                else
//                    return px + 5;
//            }
//            else
//            {
//                if (px <= 10)
//                    return px - (decimal)0.01;
//                else if (px <= 50)
//                    return px - (decimal)0.05;
//                else if (px <= 100)
//                    return px - (decimal)0.1;
//                else if (px <= 500)
//                    return px - (decimal)0.5;
//                else if (px <= 1000)
//                    return px - 1;
//                else
//                    return px - 5;
//            }
//        }
//        private static decimal NextETF(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                if (px < 50)
//                    return px + (decimal)0.01;
//                else
//                    return px + (decimal)0.05;
//            }
//            else
//            {
//                if (px <= 50)
//                    return px - (decimal)0.01;
//                else
//                    return px - (decimal)0.05;
//            }
//        }
//        private static decimal NextSTO(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                if (px < 5)
//                    return px + (decimal)0.01;
//                else if (px < 15)
//                    return px + (decimal)0.05;
//                else if (px < 50)
//                    return px + (decimal)0.1;
//                else if (px < 150)
//                    return px + (decimal)0.5;
//                else if (px < 1000)
//                    return px + 1;
//                else
//                    return px + 5;
//            }
//            else
//            {
//                if (px <= 5)
//                    return px - (decimal)0.01;
//                else if (px <= 15)
//                    return px - (decimal)0.05;
//                else if (px <= 50)
//                    return px - (decimal)0.1;
//                else if (px <= 150)
//                    return px - (decimal)0.5;
//                else if (px <= 1000)
//                    return px - 1;
//                else
//                    return px - 5;
//            }
//        }
//        private static decimal NextTEO(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                if (px < (decimal)0.5)
//                    return px + (decimal)0.005;
//                else if (px < (decimal)2.5)
//                    return px + (decimal)0.025;
//                else if (px < 25)
//                    return px + (decimal)0.05;
//                else if (px < 50)
//                    return px + (decimal)0.25;
//                else
//                    return px + (decimal)0.5;
//            }
//            else
//            {
//                if (px <= (decimal)0.5)
//                    return px - (decimal)0.005;
//                else if (px <= (decimal)2.5)
//                    return px - (decimal)0.025;
//                else if (px <= 25)
//                    return px - (decimal)0.05;
//                else if (px <= 50)
//                    return px - (decimal)0.25;
//                else
//                    return px - (decimal)0.5;
//            }
//        }
//        private static decimal NextTFO(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                if (px < 2)
//                    return px + (decimal)0.02;
//                else if (px < 10)
//                    return px + (decimal)0.1;
//                else if (px < 100)
//                    return px + (decimal)0.2;
//                else if (px < 200)
//                    return px + 1;
//                else
//                    return px + 2;
//            }
//            else
//            {
//                if (px <= 2)
//                    return px - (decimal)0.02;
//                else if (px <= 10)
//                    return px - (decimal)0.1;
//                else if (px <= 100)
//                    return px - (decimal)0.2;
//                else if (px <= 200)
//                    return px - 1;
//                else
//                    return px - 2;
//            }
//        }
//        private static decimal NextXIO(decimal px, bool goUp)
//        {
//            if (goUp)
//            {
//                if (px < 20)
//                    return px + (decimal)0.2;
//                else if (px < 100)
//                    return px + 1;
//                else if (px < 1000)
//                    return px + 2;
//                else if (px < 2000)
//                    return px + 10;
//                else
//                    return px + 20;
//            }
//            else
//            {
//                if (px <= 20)
//                    return px - (decimal)0.2;
//                else if (px <= 100)
//                    return px - 1;
//                else if (px <= 1000)
//                    return px - 2;
//                else if (px <= 2000)
//                    return px - 10;
//                else
//                    return px - 20;
//            }
//        }
//    }
//    public enum TickTypeEnum
//    {
//        [Description("10")]
//        tt100 = 0,//JNI
//        [Description("5")]
//        tt50 = 1,//SSI
//        [Description("2.5")]
//        tt25 = 2,//A50
//        [Description("1")]
//        tt1 = 3, //TXF T5F XIF I5F UDF 
//        [Description("0.5")]
//        tt05 = 4,//TGF TGO BRF
//        [Description("0.25")]
//        tt025 = 5,//TJF SPF
//        [Description("0.2")]
//        tt02 = 6, //FXF
//        [Description("0.125")]
//        tt0125 = 7,
//        [Description("0.1")]
//        tt01 = 8,//GDF
//        [Description("0.05")]
//        tt005 = 9,//EXF GTF
//        [Description("0.025")]
//        tt0025 = 10,
//        [Description("0.02")]
//        tt002 = 11,
//        [Description("0.01")]
//        tt001 = 12,//XJF
//        [Description("0.005")]
//        tt0005 = 13, //GBF
//        [Description("0.0025")]
//        tt00025 = 14,
//        [Description("0.001")]
//        tt0001 = 15, //RTF RHF XEF RTO RHO
//        [Description("0.0001")]
//        tt00001 = 16, //RTF RHF XEF RTO RHO
//        [Description("0.00005")]
//        tt000005 = 17,
//        [Description("0.000005")]
//        tt0000005 = 18,
//        [Description("0.0000005")]
//        tt00000005 = 19,
//        [Description("TXO")]
//        ttTXO = 20,//TXO
//        [Description("STF")]
//        ttSTF = 21,//STF STK
//        [Description("ETF")]
//        ttETF = 22,//ETF
//        [Description("STO")]
//        ttSTO = 23,//STO
//        [Description("TEO/GTO")]
//        ttTEO = 24,//TEO GTO
//        [Description("TFO")]
//        ttTFO = 25,//TFO
//        [Description("XIO")]
//        ttXIO = 26//XIO

//    }
//    public enum QuoteTypeEnum
//    {
//        [Description("TXO")]
//        qtTXO = 0,//TXO
//        [Description("RTO")]
//        qtRTO = 1,//RHO RTO
//        [Description("RTF")]
//        qtRTF = 2,//RHF RTF
//        [Description("XEF")]
//        qtXEF = 3,//XEF XJF
//        [Description("SPF")]
//        qtSPF = 4, //SPF
//        [Description("TGF")]
//        qtTGF = 5, //TGF
//        [Description("GDF")]
//        qtGDF = 6, //GDF
//        [Description("I5F")]
//        qtI5F = 7, //I5F
//        [Description("TJF")]
//        qtTJF = 8,
//        [Description("T5F")]
//        qtT5F = 9,
//        [Description("GTF")]
//        qtGTF = 10,
//        [Description("XIF")]
//        qtXIF = 11,
//        [Description("STF")]
//        qtSTF = 12,
//        [Description("TGO")]
//        qtTGO = 13,
//        [Description("GRO")]
//        qtGTO = 14,
//        [Description("XIO")]
//        qtXIO = 15,
//        [Description("STO")]
//        qtSTO = 16,
//        [Description("TEO")]
//        qtTEO = 17,
//        [Description("TFO")]
//        qtTFO = 18,
//        [Description("UDF")]
//        qtUDF = 19,
//        [Description("XJF")]
//        qtXJF = 20,
//        [Description("GBF")]
//        qtGBF = 21,
//        [Description("ETO")]
//        qtETO = 22,
//        [Description("None")]
//        qtNone = 99
//    }
//}
