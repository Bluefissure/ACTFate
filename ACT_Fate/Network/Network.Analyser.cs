using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace App
{
    internal partial class Network
    {
        private bool NetCompatibility;
        private State state = State.IDLE;
        private int lastMember = 0;
        
        private void AnalyseFFXIVPacket(byte[] payload)
        {
            try {
                while (true)
                {
                    if (payload.Length < 4)
                    {
                        break;
                    }

                    var type = BitConverter.ToUInt16(payload, 0);

                    if (type == 0x0000 || type == 0x5252)
                    {
                        if (payload.Length < 28)
                        {
                            break;
                        }

                        var length = BitConverter.ToInt32(payload, 24);

                        if (length <= 0 || payload.Length < length)
                        {
                            break;
                        }

                        using (var messages = new MemoryStream(payload.Length))
                        {
                            using (var stream = new MemoryStream(payload, 0, length))
                            {
                                stream.Seek(40, SeekOrigin.Begin);

                                if (payload[33] == 0x00)
                                {
                                    stream.CopyTo(messages);
                                }
                                else {
                                    stream.Seek(2, SeekOrigin.Current); // .Net DeflateStream Bug (Force the previous 2 bytes)

                                    using (var z = new DeflateStream(stream, CompressionMode.Decompress))
                                    {
                                        z.CopyTo(messages);
                                    }
                                }
                            }
                            messages.Seek(0, SeekOrigin.Begin);

                            var messageCount = BitConverter.ToUInt16(payload, 30);
                            for (var i = 0; i < messageCount; i++)
                            {
                                try
                                {
                                    var buffer = new byte[4];
                                    var read = messages.Read(buffer, 0, 4);
                                    if (read < 4)
                                    {
                                        //Log.E("l-analyze-error-length", read, i, messageCount);
                                        break;
                                    }
                                    var messageLength = BitConverter.ToInt32(buffer, 0);

                                    var message = new byte[messageLength];
                                    messages.Seek(-4, SeekOrigin.Current);
                                    messages.Read(message, 0, messageLength);

                                    HandleMessage(message);
                                }
                                catch (Exception ex)
                                {
                                    Log.Ex(ex, "l-analyze-error-general");
                                }
                            }
                        }

                        if (length < payload.Length)
                        {
                            // There are more packets left to process
                            payload = payload.Skip(length).ToArray();
                            continue;
                        }
                    }
                    else
                    {
                        // Packets coming out of the front workaround
                        // Discard one truncated packet and find just the next packet ...
                        // TODO: Correctly make no discarded packets

                        for (var offset = 0; offset < payload.Length - 2; offset++)
                        {
                            var possibleType = BitConverter.ToUInt16(payload, offset);
                            if (possibleType == 0x5252)
                            {
                                payload = payload.Skip(offset).ToArray();
                                AnalyseFFXIVPacket(payload);
                                break;
                            }
                        }
                    }

                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Ex(ex, "l-analyze-error");
            }
        }

        private void HandleMessage(byte[] message)
        {
            try
            {
                if (message.Length < 32)
                {
                    // type == 0x0000 Messages were filtered here
                    return;
                }
                
                var opcode = BitConverter.ToUInt16(message, 18);

#if !DEBUG
                if (opcode != 0x0078 &&
                    opcode != 0x0079 &&
                    opcode != 0x0080 &&
                    opcode != 0x006C &&
                    opcode != 0x006F &&
                    opcode != 0x0121 &&
                    opcode != 0x0143 &&
                    opcode != 0x022F)
                    return;
#endif

                var data = message.Skip(32).ToArray();

                // Entry / exit
                if (opcode == 0x022F) 
                {
                    var code = BitConverter.ToInt16(data, 4);
                    var type = data[8];

                    Log.B(data);

                    if (type == 0x0B)
                    {
                        //Log.I("l-field-instance-entered", Data.GetInstance(code).Name);
                        // EVENT: When worn, code = dungeon code
                        // 
                        //Log.I("l-field-instance-entered " + code);
                        Log.I("Incoming code=" + code);
                        fireEvent(EventType.INSTANCE_ENTER, new int[] { code });
                    }
                    else if (type == 0x0C)
                    {
                        // EVENT: Come out of dress
                        //Log.I("l-field-instance-left");
                        Log.I("I'm leaving=" + code);
                        fireEvent(EventType.INSTANCE_EXIT, new int[] { code });
                    }

                    
                }
                //Outbreak: occurring, in progress, ending
                else if (opcode == 0x0143)
                {
                    var type = data[0];

                    if (type == 0x9B)
                    {
                        var code = BitConverter.ToUInt16(data, 4);
                        var progress = data[8];
                        /*

                        var fate = Data.GetFATE(code);

                        //Log.D("\"{0}\" Breakthrough progress {1}%", fate.Name, progress);
                        */
                        // EVENT: breakthrough, code = breakthrough, progress = progress percentage
                        //Log.I(code + " Breakthrough progress " + progress);
                        fireEvent(EventType.FATE_PROGRESS, new int[] { code, progress });
                    }
                    else if (type == 0x79)
                    {

                        // Unexpected mission termination (for all missions that may occur when moving the area)

                        var code = BitConverter.ToUInt16(data, 4);
                        var status = BitConverter.ToUInt16(data, 28);

                        /*
                        var fate = Data.GetFATE(code);

                        //Log.D("\"{0}\" Break out!", fate.Name);
                        */
                        // EVENT: At the end of the mission, code = emergency code,
                        Log.I("Abrupt end=" + code + ", status=" + status);
                        fireEvent(EventType.FATE_END, new int[] { code, status });
                    }
                    else if (type == 0x74)
                    {
                        // Occurrence of an outbreak (even if you move the area, the existing breakdown list comes)
                        // EVENT: Occurrence of an unexpected mission, code = Unexpected code (also occurs when moving map)
                        var code = BitConverter.ToUInt16(data, 4);
                        Log.I("Abrupt =" + code);
                        //var fate = Data.GetFATE(code);
                        fireEvent(EventType.FATE_BEGIN, new int[] { code });
                    }
                }
                // Matching information
                else if (opcode == 0x0078)
                {
                    var status = data[0];
                    var reason = data[4];

                    //apply
                    if (status == 0)
                    {
                        NetCompatibility = false;
                        state = State.QUEUED;

                        var rouletteCode = data[20];

                        if (rouletteCode != 0 && (data[15] == 0 || data[15] == 64)) //Random assignment application, Korea server / global server
                        {
                            //var roulette = Data.GetRoulette(rouletteCode);
                            //mainForm.overlayForm.SetRoulleteDuty(roulette);
                            //Log.I("l-queue-started-roulette", roulette.Name);
                            Log.I("Matching application, random type=" + rouletteCode);
                            fireEvent(EventType.MATCH_BEGIN, new int[] { (int)MatchType.ROULETTE, rouletteCode });
                        }
                        else //Apply for a specific mission
                        {
                            // var instances = new List<Instance>();
                            var instances = new List<int>();
                            for (int i = 0; i < 5; i++)
                            {
                                var code = BitConverter.ToUInt16(data, 22 + (i * 2));
                                if (code == 0)
                                {
                                    break;
                                }
                                //instances.Add(Data.GetInstance(code));
                                instances.Add(code);
                            }
                            
                            if (!instances.Any())
                            {
                                return;
                            }

                            var args = new List<int>();
                            args.Add((int)MatchType.SELECTIVE);
                            args.Add(instances.Count);
                            for (int i = 0; i < instances.Count; i++) args.Add(instances[i]);

                            // Log.I("l-queue-started-general",
                            //string.Join(", ", instances.Select(x => x.Name).ToArray()));*/
                            Log.I("Matching application, selected =", string.Join(", ", instances) + ", count=" + instances.Count);
                            fireEvent(EventType.MATCH_BEGIN, args.ToArray());
                        }
                    }
                    // cancel
                    else if (status == 3)
                    {
                        state = reason == 8 ? State.QUEUED : State.IDLE;
                        //mainForm.overlayForm.CancelDutyFinder();

                        //Log.E("l-queue-stopped");
                        Log.I("Unmatch, reason=" + reason);
                        fireEvent(EventType.MATCH_END, new int[] { (int)MatchEndType.CANCELLED });
                    }
                    // Entered
                    else if (status == 6)
                    {
                        state = State.IDLE;
                        //mainForm.overlayForm.CancelDutyFinder();

                        //Log.I("l-queue-entered");
                        Log.I("Match entry");
                        fireEvent(EventType.MATCH_END, new int[] { (int)MatchEndType.ENTER_INSTANCE });
                    }
                    // Matching
                    else if (status == 4) // Output when matched in the foreground
                    {
                        var roulette = data[20];
                        var code = BitConverter.ToUInt16(data, 22);

                        //Instance instance;

                        //If the matched indent information can not be checked,
                        /*
                        if (roulette != 0)
                        {
                            instance = new Instance { Name = Data.GetRoulette(roulette).Name };
                        }
                        else
                        {
                            instance = Data.GetInstance(code);
                        }
                        */

                        state = State.MATCHED;
                       

                        Log.I("Matching (Gloss) Match type=" + roulette + ", Matched indones=" + code);
                        fireEvent(EventType.MATCH_ALERT, new int[] { roulette, code });
                    }
                }
                else if (opcode == 0x006F)
                {
                    /*
                    var status = data[0];

                    if (status == 0)
                    {
                        // Player clicks Cancel in the Match Participation Confirmation window or the Participation Confirmation Timeout is exceeded
                         // Upper 2db status 3 packets coming in to notify matching termination
                        log.i("Matched stopped status=" + status + ", cancel or timeout");
                    }
                    if (status == 1)
                    {
                        // Player clicks OK in the matching entry confirmation window
                        // If all other matching personnel have clicked OK, the top 2db status 6 packets for admission
                        //mainform.overlayform.stopblink();
                        log.i("Matched status =" + status + ", press confirm or others);
                    }*/
                }
                else if (opcode == 0x0121) // Global server
                {
                    var status = data[5];

                    if (status == 128)
                    {
                        //Click OK on the Matching Application Confirmation window
                        //mainForm.overlayForm.StopBlink();
                        Log.I("Matching, Matching Click OK in the application confirmation window (Global)");
                    }
                }
                // Status in matched state
                else if (opcode == 0x0079)
                {
                    var code = BitConverter.ToUInt16(data, 0);
                    byte order = 0;
                    byte status = 0;
                    byte tank = 0;
                    byte dps = 0;
                    byte healer = 0;
                    if (NetCompatibility) // V4.5 版本兼容性
                    {
                        order = data[4]; //职能等待顺序
                        order--;
                        status = data[8];
                        tank = data[9];
                        dps = data[10];
                        healer = data[11];
                    }
                    else
                    {
                        order = data[5];
                        status = data[4];
                        tank = data[5];
                        dps = data[6];
                        healer = data[7];
                    }
                    
                    if (status == 0 && tank == 0 && healer == 0 && dps == 0) // 检查数据异常进行兼容处理
                    {
                        NetCompatibility = true;
                        order = 255;
                        status = data[8];
                        tank = data[9];
                        dps = data[10];
                        healer = data[11];
                    }
                    
                    //var instance = Data.GetInstance(code);

                    if (status == 1)
                    {
                        // Personnel status packet
                        var member = tank * 10000 + dps * 100 + healer;

                        if (state == State.MATCHED && lastMember != member)
                        {
                            // If the queue status is canceled by someone when the status packet arrives at the time of matching and is different from the last person information.
                            state = State.QUEUED;
                            //mainForm.overlayForm.CancelDutyFinder();
                            Log.I("Match progress, someone canceled");
                        }
                        else if (state == State.IDLE)
                        {
                            // Program is on in the middle of matching
                            state = State.QUEUED;
                            //mainForm.overlayForm.SetDutyCount(-1); // Set to unknown (TODO: If there is a way to find out, fix it to be correct)
                            //mainForm.overlayForm.SetDutyStatus(instance, tank, dps, healer);
                        }
                        else if (state == State.QUEUED)
                        {
                            //mainForm.overlayForm.SetDutyStatus(instance, tank, dps, healer);
                        }

                        lastMember = member;
                    }
                    else if (status == 2)
                    {
                        // Information on the number of people by role of the currently matched party
                        // Even if it is in un-tuned state,
                        //mainForm.overlayForm.SetMemberCount(tank, dps, healer);
                        return;
                    }
                    else if (status == 4)
                    {
                        // After Matching, Participant Confirmation Status Packet
                        //mainForm.overlayForm.SetConfirmStatus(instance, tank, dps, healer);
                    }
                    //Log.I("l-queue-updated", instance.Name, status, tank, instance.Tank, healer, instance.Healer, dps, instance.DPS);
                    Log.I("Matching progress, code=" + code + ", " + status + ", T " + tank + ", H " + healer + ", D " + dps);
                    fireEvent(EventType.MATCH_PROGRESS, new int[] { code, status, tank, healer, dps });
                }
                else if (opcode == 0x0080)
                {
                    var roulette = data[2];
                    var code = BitConverter.ToUInt16(data, 4);

                    //Instance instance;

                    if (roulette != 0)
                    {
                    //    instance = new Instance { Name = Data.GetRoulette(roulette).Name };
                    }
                    else
                    {
                    //    instance = Data.GetInstance(code);
                    }

                    state = State.MATCHED;

                    Log.S("l-queue-matched " + code);
                    fireEvent(EventType.MATCH_ALERT, new int[] { roulette, code });
                }
            }
            catch (Exception ex)
            {
                Log.Ex(ex, "[" + pid + "]l-analyze-error-general");
            }
        }

        private enum State
        {
            IDLE,
            QUEUED,
            MATCHED,
        }
    }
}
