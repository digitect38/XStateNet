using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace XStateNet.Semi.Secs
{
    /// <summary>
    /// Library of common SECS-II messages as defined in SEMI E5
    /// </summary>
    public static class SecsMessageLibrary
    {
        #region Stream 1 - Equipment Status
        
        /// <summary>
        /// S1F1 - Are You There Request
        /// </summary>
        public static SecsMessage S1F1()
        {
            return new SecsMessage(1, 1, true);
        }
        
        /// <summary>
        /// S1F2 - On Line Data
        /// </summary>
        public static SecsMessage S1F2(string mdln = "", string softRev = "")
        {
            return new SecsMessage(1, 2, false)
            {
                Data = new SecsList(
                    new SecsAscii(mdln),    // Model name
                    new SecsAscii(softRev)   // Software revision
                )
            };
        }
        
        /// <summary>
        /// S1F3 - Selected Equipment Status Request
        /// </summary>
        public static SecsMessage S1F3(params uint[] svids)
        {
            if (svids.Length == 0)
            {
                return new SecsMessage(1, 3, true)
                {
                    Data = new SecsList() // Empty list for all SVIDs
                };
            }
            
            var items = new SecsItem[svids.Length];
            for (int i = 0; i < svids.Length; i++)
            {
                items[i] = new SecsU4(svids[i]);
            }
            
            return new SecsMessage(1, 3, true)
            {
                Data = new SecsList(items)
            };
        }
        
        /// <summary>
        /// S1F4 - Selected Equipment Status Data
        /// </summary>
        public static SecsMessage S1F4(List<SecsItem> statusValues)
        {
            return new SecsMessage(1, 4, false)
            {
                Data = new SecsList(statusValues.ToArray())
            };
        }
        
        /// <summary>
        /// S1F13 - Establish Communications Request
        /// </summary>
        public static SecsMessage S1F13()
        {
            return new SecsMessage(1, 13, true);
        }
        
        /// <summary>
        /// S1F14 - Establish Communications Request Acknowledge
        /// </summary>
        public static SecsMessage S1F14(byte commack = 0, string mdln = "", string softRev = "")
        {
            return new SecsMessage(1, 14, false)
            {
                Data = new SecsList(
                    new SecsU1(commack),
                    new SecsList(
                        new SecsAscii(mdln),
                        new SecsAscii(softRev)
                    )
                )
            };
        }
        
        #endregion
        
        #region Stream 2 - Equipment Control
        
        /// <summary>
        /// S2F13 - Equipment Constant Request
        /// </summary>
        public static SecsMessage S2F13(params uint[] ecids)
        {
            // Create a list of individual SecsU4 items as per SECS-II standard
            var items = new SecsItem[ecids.Length];
            for (int i = 0; i < ecids.Length; i++)
            {
                items[i] = new SecsU4(ecids[i]);
            }
            return new SecsMessage(2, 13, true)
            {
                Data = new SecsList(items)
            };
        }
        
        /// <summary>
        /// S2F14 - Equipment Constant Data
        /// </summary>
        public static SecsMessage S2F14(List<SecsItem> ecValues)
        {
            return new SecsMessage(2, 14, false)
            {
                Data = new SecsList(ecValues.ToArray())
            };
        }
        
        /// <summary>
        /// S2F15 - New Equipment Constant Send
        /// </summary>
        public static SecsMessage S2F15(ConcurrentDictionary<uint, SecsItem> ecidValues)
        {
            var items = new List<SecsItem>();
            foreach (var kvp in ecidValues)
            {
                items.Add(new SecsList(
                    new SecsU4(kvp.Key),
                    kvp.Value
                ));
            }
            
            return new SecsMessage(2, 15, true)
            {
                Data = new SecsList(items.ToArray())
            };
        }
        
        /// <summary>
        /// S2F16 - New Equipment Constant Acknowledge
        /// </summary>
        public static SecsMessage S2F16(byte eac = 0)
        {
            return new SecsMessage(2, 16, false)
            {
                Data = new SecsU1(eac)
            };
        }
        
        /// <summary>
        /// S2F41 - Host Command Send
        /// </summary>
        public static SecsMessage S2F41(string rcmd, ConcurrentDictionary<string, SecsItem> parameters)
        {
            var cpList = new List<SecsItem>();
            foreach (var kvp in parameters)
            {
                cpList.Add(new SecsList(
                    new SecsAscii(kvp.Key),
                    kvp.Value
                ));
            }
            
            return new SecsMessage(2, 41, true)
            {
                Data = new SecsList(
                    new SecsAscii(rcmd),
                    new SecsList(cpList.ToArray())
                )
            };
        }
        
        /// <summary>
        /// S2F42 - Host Command Acknowledge
        /// </summary>
        public static SecsMessage S2F42(byte hcack = 0, ConcurrentDictionary<string, SecsItem>? parameters = null)
        {
            var items = new List<SecsItem> { new SecsU1(hcack) };
            
            if (parameters != null)
            {
                var cpList = new List<SecsItem>();
                foreach (var kvp in parameters)
                {
                    cpList.Add(new SecsList(
                        new SecsAscii(kvp.Key),
                        kvp.Value
                    ));
                }
                items.Add(new SecsList(cpList.ToArray()));
            }
            else
            {
                items.Add(new SecsList());
            }
            
            return new SecsMessage(2, 42, false)
            {
                Data = new SecsList(items.ToArray())
            };
        }
        
        #endregion
        
        #region Stream 5 - Exception Handling
        
        /// <summary>
        /// S5F1 - Alarm Report Send
        /// </summary>
        public static SecsMessage S5F1(byte alcd, uint alid, string altx = "")
        {
            return new SecsMessage(5, 1, true)
            {
                Data = new SecsList(
                    new SecsU1(alcd),    // Alarm code (128 = Set, 0 = Clear)
                    new SecsU4(alid),    // Alarm ID
                    new SecsAscii(altx)  // Alarm text
                )
            };
        }
        
        /// <summary>
        /// S5F2 - Alarm Report Acknowledge
        /// </summary>
        public static SecsMessage S5F2(byte ackc5 = 0)
        {
            return new SecsMessage(5, 2, false)
            {
                Data = new SecsU1(ackc5)
            };
        }
        
        #endregion
        
        #region Stream 6 - Data Collection
        
        /// <summary>
        /// S6F11 - Event Report Send
        /// </summary>
        public static SecsMessage S6F11(uint ceid, List<SecsItem> reports)
        {
            return new SecsMessage(6, 11, true)
            {
                Data = new SecsList(
                    new SecsU4(ceid),
                    new SecsList(reports.ToArray())
                )
            };
        }
        
        /// <summary>
        /// S6F12 - Event Report Acknowledge
        /// </summary>
        public static SecsMessage S6F12(byte ackc6 = 0)
        {
            return new SecsMessage(6, 12, false)
            {
                Data = new SecsU1(ackc6)
            };
        }
        
        /// <summary>
        /// S6F15 - Event Report Request
        /// </summary>
        public static SecsMessage S6F15(uint ceid)
        {
            return new SecsMessage(6, 15, true)
            {
                Data = new SecsU4(ceid)
            };
        }
        
        /// <summary>
        /// S6F16 - Event Report Data
        /// </summary>
        public static SecsMessage S6F16(uint ceid, List<SecsItem> reports)
        {
            return new SecsMessage(6, 16, false)
            {
                Data = new SecsList(
                    new SecsU4(ceid),
                    new SecsList(reports.ToArray())
                )
            };
        }
        
        #endregion
        
        #region Stream 7 - Process Program Management
        
        /// <summary>
        /// S7F1 - Process Program Load Inquire
        /// </summary>
        public static SecsMessage S7F1(string ppid, uint length)
        {
            return new SecsMessage(7, 1, true)
            {
                Data = new SecsList(
                    new SecsAscii(ppid),
                    new SecsU4(length)
                )
            };
        }
        
        /// <summary>
        /// S7F2 - Process Program Load Grant
        /// </summary>
        public static SecsMessage S7F2(byte ppgnt = 0)
        {
            return new SecsMessage(7, 2, false)
            {
                Data = new SecsU1(ppgnt)
            };
        }
        
        /// <summary>
        /// S7F3 - Process Program Send
        /// </summary>
        public static SecsMessage S7F3(string ppid, byte[] ppbody)
        {
            return new SecsMessage(7, 3, true)
            {
                Data = new SecsList(
                    new SecsAscii(ppid),
                    new SecsBinary(ppbody)
                )
            };
        }
        
        /// <summary>
        /// S7F4 - Process Program Acknowledge
        /// </summary>
        public static SecsMessage S7F4(byte ackc7 = 0)
        {
            return new SecsMessage(7, 4, false)
            {
                Data = new SecsU1(ackc7)
            };
        }
        
        /// <summary>
        /// S7F5 - Process Program Request
        /// </summary>
        public static SecsMessage S7F5(string ppid)
        {
            return new SecsMessage(7, 5, true)
            {
                Data = new SecsAscii(ppid)
            };
        }
        
        /// <summary>
        /// S7F6 - Process Program Data
        /// </summary>
        public static SecsMessage S7F6(string ppid, byte[] ppbody)
        {
            return new SecsMessage(7, 6, false)
            {
                Data = new SecsList(
                    new SecsAscii(ppid),
                    new SecsBinary(ppbody)
                )
            };
        }
        
        /// <summary>
        /// S7F17 - Delete Process Program Send
        /// </summary>
        public static SecsMessage S7F17(params string[] ppids)
        {
            var items = new List<SecsItem>();
            foreach (var ppid in ppids)
            {
                items.Add(new SecsAscii(ppid));
            }
            
            return new SecsMessage(7, 17, true)
            {
                Data = new SecsList(items.ToArray())
            };
        }
        
        /// <summary>
        /// S7F18 - Delete Process Program Acknowledge
        /// </summary>
        public static SecsMessage S7F18(byte ackc7 = 0)
        {
            return new SecsMessage(7, 18, false)
            {
                Data = new SecsU1(ackc7)
            };
        }
        
        #endregion
        
        #region Stream 10 - Terminal Services
        
        /// <summary>
        /// S10F1 - Terminal Request
        /// </summary>
        public static SecsMessage S10F1(byte tid, string text)
        {
            return new SecsMessage(10, 1, true)
            {
                Data = new SecsList(
                    new SecsU1(tid),
                    new SecsAscii(text)
                )
            };
        }
        
        /// <summary>
        /// S10F2 - Terminal Request Acknowledge
        /// </summary>
        public static SecsMessage S10F2(byte ackc10 = 0)
        {
            return new SecsMessage(10, 2, false)
            {
                Data = new SecsU1(ackc10)
            };
        }
        
        /// <summary>
        /// S10F3 - Terminal Display Single
        /// </summary>
        public static SecsMessage S10F3(byte tid, string text)
        {
            return new SecsMessage(10, 3, true)
            {
                Data = new SecsList(
                    new SecsU1(tid),
                    new SecsAscii(text)
                )
            };
        }
        
        /// <summary>
        /// S10F4 - Terminal Display Single Acknowledge
        /// </summary>
        public static SecsMessage S10F4(byte ackc10 = 0)
        {
            return new SecsMessage(10, 4, false)
            {
                Data = new SecsU1(ackc10)
            };
        }
        
        #endregion
        
        #region Common Response Codes
        
        public static class ResponseCodes
        {
            // COMMACK values for S1F14
            public const byte COMMACK_ACCEPTED = 0;
            public const byte COMMACK_DENIED = 1;
            
            // EAC values for S2F16
            public const byte EAC_ACCEPTED = 0;
            public const byte EAC_DENIED_ONE_OR_MORE = 1;
            public const byte EAC_DENIED_BUSY = 2;
            public const byte EAC_DENIED_ALL_CONSTANTS = 3;
            
            // HCACK values for S2F42
            public const byte HCACK_OK = 0;
            public const byte HCACK_INVALID_COMMAND = 1;
            public const byte HCACK_CANNOT_PERFORM_NOW = 2;
            public const byte HCACK_PARAMETER_ERROR = 3;
            public const byte HCACK_INITIATED = 4;
            public const byte HCACK_REJECTED = 5;
            public const byte HCACK_INVALID_OBJECT = 6;
            
            // ACKC5 values for S5F2
            public const byte ACKC5_ACCEPTED = 0;
            public const byte ACKC5_NOT_ACCEPTED = 1;
            
            // ACKC6 values for S6F12
            public const byte ACKC6_ACCEPTED = 0;
            public const byte ACKC6_NOT_ACCEPTED = 1;
            
            // PPGNT values for S7F2
            public const byte PPGNT_OK = 0;
            public const byte PPGNT_ALREADY_HAVE = 1;
            public const byte PPGNT_NO_SPACE = 2;
            public const byte PPGNT_INVALID_PPID = 3;
            public const byte PPGNT_BUSY = 4;
            public const byte PPGNT_WILL_NOT_ACCEPT = 5;
            public const byte PPGNT_OTHER_ERROR = 6;
            
            // ACKC7 values for S7F4/S7F18
            public const byte ACKC7_ACCEPTED = 0;
            public const byte ACKC7_PERMISSION_NOT_GRANTED = 1;
            public const byte ACKC7_LENGTH_ERROR = 2;
            public const byte ACKC7_MATRIX_OVERFLOW = 3;
            public const byte ACKC7_PPID_NOT_FOUND = 4;
            public const byte ACKC7_MODE_UNSUPPORTED = 5;
            public const byte ACKC7_COMMAND_WILL_BE_PERFORMED = 6;
            public const byte ACKC7_DATA_ERROR = 7;
            
            // ACKC10 values for S10F2/S10F4
            public const byte ACKC10_ACCEPTED = 0;
            public const byte ACKC10_WILL_NOT_DISPLAY = 1;
            public const byte ACKC10_TERMINAL_NOT_AVAILABLE = 2;
        }
        
        #endregion
    }
}