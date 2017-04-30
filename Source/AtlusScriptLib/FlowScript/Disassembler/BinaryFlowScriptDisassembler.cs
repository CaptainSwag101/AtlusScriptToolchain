﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AtlusScriptLib.Utilities;
using System.Globalization;

namespace AtlusScriptLib.FlowScript.Disassembler
{
    public class BinaryFlowScriptDisassembler
    {
        private string m_HeaderString = "This file was generated by AtlusScriptLib";
        private BinaryFlowScript m_Script;
        private IDisassemblerTextOutput m_Output;     
        private int m_InstructionIndex;

        public string HeaderString
        {
            get { return m_HeaderString; }
            set { m_HeaderString = value; }
        }

        private BinaryFlowScriptInstruction CurrentInstruction
        {
            get
            {
                if (m_Script == null || m_Script.TextSectionData == null || m_Script.TextSectionData.Count == 0)
                    throw new InvalidDataException("Invalid state");

                return m_Script.TextSectionData[m_InstructionIndex];
            }
        }

        private BinaryFlowScriptInstruction? NextInstruction
        {
            get
            {
                if (m_Script == null || m_Script.TextSectionData == null || m_Script.TextSectionData.Count == 0)
                    return null;

                if ((m_InstructionIndex + 1) < (m_Script.TextSectionData.Count - 1))
                    return m_Script.TextSectionData[m_InstructionIndex + 1];
                else
                    return null;
            }
        }

        public BinaryFlowScriptDisassembler(StringBuilder stringBuilder)
        {
            m_Output = new StringBuilderDisassemblerTextOutput(stringBuilder);
        }

        public BinaryFlowScriptDisassembler(TextWriter writer)
        {
            m_Output = new TextWriterDisassemblerTextOutput(writer);
        }

        public BinaryFlowScriptDisassembler(string outpath)
        {
            m_Output = new TextWriterDisassemblerTextOutput(new StreamWriter(outpath));
        }

        public BinaryFlowScriptDisassembler(Stream stream)
        {
            m_Output = new TextWriterDisassemblerTextOutput(new StreamWriter(stream));
        }

        public void Disassemble(BinaryFlowScript script)
        {
            m_Script = script ?? throw new ArgumentNullException(nameof(script));
            m_InstructionIndex = 0;

            PutDisassembly();
        }

        private void PutHeader()
        {
            m_Output.PutCommentLine(m_HeaderString);
            m_Output.PutNewline();
        }

        private void PutDisassembly()
        {
            PutHeader();
            PutTextDisassembly();
            PutMessageScriptDisassembly();
        }

        private void PutTextDisassembly()
        {
            m_Output.PutLine(".text");

            while (m_InstructionIndex < m_Script.TextSectionData.Count)
            {
                // Check if there is a possible jump label at the current index
                var jumps = m_Script.JumpLabelSectionData.Where(x => x.Offset == m_InstructionIndex);

                foreach (var jump in jumps)
                {
                    m_Output.PutLine($"{jump.Name}:");
                }

                PutInstructionDisassembly();
            }

            m_Output.PutNewline();
        }

        private void PutInstructionDisassembly()
        {
            int usedInstructions = 1;

            switch (CurrentInstruction.Opcode)
            {
                // extended int operand
                case BinaryFlowScriptOpcode.PUSHI:
                    usedInstructions = 2;
                    m_Output.PutLine(DisassembleInstructionWithIntOperand(CurrentInstruction, NextInstruction.Value));
                    break;

                // extended float operand
                case BinaryFlowScriptOpcode.PUSHF:
                    usedInstructions = 2;
                    m_Output.PutLine(DisassembleInstructionWithFloatOperand(CurrentInstruction, NextInstruction.Value));
                    break;

                // short operand
                case BinaryFlowScriptOpcode.PUSHIX:
                case BinaryFlowScriptOpcode.PUSHIF:
                case BinaryFlowScriptOpcode.POPIX:
                case BinaryFlowScriptOpcode.POPFX:
                case BinaryFlowScriptOpcode.RUN:
                case BinaryFlowScriptOpcode.PUSHIS:
                case BinaryFlowScriptOpcode.PUSHLIX:
                case BinaryFlowScriptOpcode.PUSHLFX:
                case BinaryFlowScriptOpcode.POPLIX:
                case BinaryFlowScriptOpcode.POPLFX:
                    m_Output.PutLine(DisassembleInstructionWithShortOperand(CurrentInstruction));
                    break;

                // string opcodes
                case BinaryFlowScriptOpcode.PUSHSTR:
                    m_Output.PutLine(DisassembleInstructionWithStringReferenceOperand(CurrentInstruction, m_Script.StringSectionData));
                    break;

                // branch procedure opcodes
                case BinaryFlowScriptOpcode.PROC:
                case BinaryFlowScriptOpcode.CALL:
                    m_Output.PutLine(DisassembleInstructionWithLabelReferenceOperand(CurrentInstruction, m_Script.ProcedureLabelSectionData));
                    break;

                // branch jump opcodes
                case BinaryFlowScriptOpcode.JUMP:           
                case BinaryFlowScriptOpcode.GOTO:
                case BinaryFlowScriptOpcode.IF:
                    m_Output.PutLine(DisassembleInstructionWithLabelReferenceOperand(CurrentInstruction, m_Script.JumpLabelSectionData));
                    break;

                // branch communicate opcode
                case BinaryFlowScriptOpcode.COMM:
                    m_Output.PutLine(DisassembleInstructionWithCommReferenceOperand(CurrentInstruction));
                    break;

                // No operands
                case BinaryFlowScriptOpcode.PUSHREG:          
                case BinaryFlowScriptOpcode.ADD:
                case BinaryFlowScriptOpcode.SUB:               
                case BinaryFlowScriptOpcode.MUL:
                case BinaryFlowScriptOpcode.DIV:
                case BinaryFlowScriptOpcode.MINUS:
                case BinaryFlowScriptOpcode.NOT:
                case BinaryFlowScriptOpcode.OR:
                case BinaryFlowScriptOpcode.AND:
                case BinaryFlowScriptOpcode.EQ:
                case BinaryFlowScriptOpcode.NEQ:
                case BinaryFlowScriptOpcode.S:
                case BinaryFlowScriptOpcode.L:
                case BinaryFlowScriptOpcode.SE:
                case BinaryFlowScriptOpcode.LE:
                    m_Output.PutLine(DisassembleInstructionWithNoOperand(CurrentInstruction));
                    break;

                case BinaryFlowScriptOpcode.END:
                    m_Output.PutLine(DisassembleInstructionWithNoOperand(CurrentInstruction));
                    if (NextInstruction.HasValue)
                    {
                        if (NextInstruction.Value.Opcode != BinaryFlowScriptOpcode.END)
                            m_Output.PutNewline();
                    }
                    break;

                default:
                    DebugUtils.FatalException($"Unknown opcode {CurrentInstruction.Opcode}");
                    break;
            }

            m_InstructionIndex += usedInstructions;
        }

        private void PutMessageScriptDisassembly()
        {
            m_Output.PutLine(".msgdata raw");
            for (int i = 0; i < m_Script.MessageScriptSectionData.Count; i++)
            {
                m_Output.Put(m_Script.MessageScriptSectionData[i].ToString("X2"));
            }
        }

        public static string DisassembleInstructionWithNoOperand(BinaryFlowScriptInstruction instruction)
        {
            if (instruction.OperandShort != 0)
            {
                DebugUtils.TraceError($"{instruction.Opcode} should not have any operands");
            }

            return $"{instruction.Opcode}";
        }

        public static string DisassembleInstructionWithIntOperand(BinaryFlowScriptInstruction instruction, BinaryFlowScriptInstruction operand)
        {
            return $"{instruction.Opcode} {operand.OperandInt}";
        }

        public static string DisassembleInstructionWithFloatOperand(BinaryFlowScriptInstruction instruction, BinaryFlowScriptInstruction operand)
        {
            return $"{instruction.Opcode} {operand.OperandFloat.ToString("0.00#####", CultureInfo.InvariantCulture)}f";
        }

        public static string DisassembleInstructionWithShortOperand(BinaryFlowScriptInstruction instruction)
        {
            return $"{instruction.Opcode} {instruction.OperandShort}";
        }

        public static string DisassembleInstructionWithStringReferenceOperand(BinaryFlowScriptInstruction instruction, IDictionary<int, string> stringMap)
        {
            if (!stringMap.ContainsKey(instruction.OperandShort))
            {
                DebugUtils.FatalException($"No string for string reference id {instruction.OperandShort} present in {nameof(stringMap)}");
            }

            return $"{instruction.Opcode} \"{stringMap[instruction.OperandShort]}\"";
        }

        public static string DisassembleInstructionWithLabelReferenceOperand(BinaryFlowScriptInstruction instruction, IList<BinaryFlowScriptLabel> labels)
        {
            if (instruction.OperandShort >= labels.Count)
            {
                DebugUtils.FatalException($"No label for label reference id {instruction.OperandShort} present in {nameof(labels)}");
            }

            return $"{instruction.Opcode} {labels[instruction.OperandShort].Name}";
        }

        public static string DisassembleInstructionWithCommReferenceOperand(BinaryFlowScriptInstruction instruction)
        {
            return $"{instruction.Opcode} {instruction.OperandShort}";
        }
    }
}
