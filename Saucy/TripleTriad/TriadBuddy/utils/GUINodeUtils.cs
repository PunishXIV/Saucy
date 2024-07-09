using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using TriadBuddyPlugin;

namespace MgAl2O4.Utils
{
    // Dalamud.Interface.UIDebug is amazing

    public class GUINodeUtils
    {
        public static unsafe AtkResNode* PickChildNode(AtkResNode* maybeCompNode, int childIdx, int expectedNumChildren)
        {
            if (maybeCompNode != null && (int)maybeCompNode->Type >= 1000)
            {
                var compNode = (AtkComponentNode*)maybeCompNode;
                if (compNode->Component->UldManager.NodeListCount == expectedNumChildren && childIdx < expectedNumChildren)
                {
                    return compNode->Component->UldManager.NodeList[childIdx];
                }
            }

            return null;
        }

        public static unsafe AtkResNode* PickChildNode(AtkComponentBase* compPtr, int childIdx, int expectedNumChildren)
        {
            if (compPtr != null && compPtr->UldManager.NodeListCount == expectedNumChildren && childIdx < expectedNumChildren)
            {
                return compPtr->UldManager.NodeList[childIdx];
            }

            return null;
        }

        public static unsafe AtkResNode*[]? GetImmediateChildNodes(AtkResNode* node)
        {
            var listAddr = new List<ulong>();
            if (node != null && node->ChildNode != null)
            {
                listAddr.Add((ulong)node->ChildNode);

                node = node->ChildNode;
                while (node->PrevSiblingNode != null)
                {
                    listAddr.Add((ulong)node->PrevSiblingNode);
                    node = node->PrevSiblingNode;
                }
            }

            return ConvertToNodeArr(listAddr);
        }

        public static unsafe AtkResNode*[]? GetAllChildNodes(AtkResNode* node)
        {
            if (node != null)
            {
                var list = new List<ulong>();
                RecursiveAppendChildNodes(node, list);

                return ConvertToNodeArr(list);
            }

            return null;
        }

        private static unsafe void RecursiveAppendChildNodes(AtkResNode* node, List<ulong> listAddr)
        {
            if (node != null)
            {
                listAddr.Add((ulong)node);

                // step inside
                if (node->ChildNode != null)
                {
                    RecursiveAppendChildNodes(node->ChildNode, listAddr);

                    AtkResNode* linkNode = node->ChildNode;
                    while (linkNode->PrevSiblingNode != null)
                    {
                        RecursiveAppendChildNodes(linkNode->PrevSiblingNode, listAddr);
                        linkNode = linkNode->PrevSiblingNode;
                    }

                    // no need to check next siblings here?
                }
            }
        }

        private static unsafe AtkResNode*[]? ConvertToNodeArr(List<ulong> listAddr)
        {
            if (listAddr.Count > 0)
            {
                var typedArr = new AtkResNode*[listAddr.Count];
                for (int idx = 0; idx < listAddr.Count; idx++)
                {
                    typedArr[idx] = (AtkResNode*)listAddr[idx];
                }

                return typedArr;
            }

            return null;
        }

        public static unsafe AtkResNode* PickNode(AtkResNode*[]? nodes, int nodeIdx, int expectedNumNodes)
        {
            if (nodes != null && nodes.Length == expectedNumNodes && nodeIdx < expectedNumNodes)
            {
                return nodes[nodeIdx];
            }

            return null;
        }

        public static unsafe AtkResNode* GetChildNode(AtkResNode* node)
        {
            return node != null ? node->ChildNode : null;
        }

        public static unsafe string? GetNodeTexturePath(AtkResNode* maybeImageNode)
        {
            if (maybeImageNode != null && maybeImageNode->Type == NodeType.Image)
            {
                var imageNode = (AtkImageNode*)maybeImageNode;
                if (imageNode->PartsList != null && imageNode->PartId <= imageNode->PartsList->PartCount)
                {
                    var textureInfo = imageNode->PartsList->Parts[imageNode->PartId].UldAsset;
                    var texType = textureInfo->AtkTexture.TextureType;
                    if (texType == TextureType.Resource)
                    {
                        var texFileNameStdString = &textureInfo->AtkTexture.Resource->TexFileResourceHandle->ResourceHandle.FileName;
                        var texString = texFileNameStdString->Length < 16
                            ? Marshal.PtrToStringAnsi((IntPtr)texFileNameStdString->Buffer)
                            : Marshal.PtrToStringAnsi((IntPtr)texFileNameStdString->BufferPtr);

                        return texString;
                    }
                }
            }

            return null;
        }

        public static unsafe string? GetNodeText(AtkResNode* maybeTextNode)
        {
            if (maybeTextNode != null && maybeTextNode->Type == NodeType.Text)
            {
                var textNode = (AtkTextNode*)maybeTextNode;
                var text = Marshal.PtrToStringUTF8(new IntPtr(textNode->NodeText.StringPtr));
                return text;
            }

            return null;
        }

        public static unsafe Vector2 GetNodePosition(AtkResNode* node)
        {
            var pos = new Vector2(node->X, node->Y);
            var par = node->ParentNode;
            while (par != null)
            {
                pos *= new Vector2(par->ScaleX, par->ScaleY);
                pos += new Vector2(par->X, par->Y);
                par = par->ParentNode;
            }

            return pos;
        }

        public static unsafe Vector2 GetNodeScale(AtkResNode* node)
        {
            if (node == null) return new Vector2(1, 1);
            var scale = new Vector2(node->ScaleX, node->ScaleY);
            while (node->ParentNode != null)
            {
                node = node->ParentNode;
                scale *= new Vector2(node->ScaleX, node->ScaleY);
            }

            return scale;
        }

        public static unsafe (Vector2, Vector2) GetNodePosAndSize(AtkResNode* node)
        {
            if (node != null)
            {
                var pos = GetNodePosition(node);
                var scale = GetNodeScale(node);
                var size = new Vector2(node->Width * scale.X, node->Height * scale.Y);

                return (pos, size);
            }

            return (Vector2.Zero, Vector2.Zero);
        }

#if DEBUG
        private class ParsableNode
        {
            public ulong nodeAddr;
            public string? content;
            public int childIdx;
            public int numChildren;
            public int depth;
            public NodeType type;
            public string? debugPath;
        }

        private static unsafe bool RecursiveAppendParsableChildNodes(AtkResNode* node, int depth, int childIdx, List<ParsableNode> list, string debugPath)
        {
            bool hasParsableChildNodes = false;
            bool hasContent = false;

            if (node != null)
            {
                // check if this node is interesting for parser (empty string is still interesting)
                string? content = GetNodeText(node);
                content = (content != null) ? content : GetNodeTexturePath(node);
                hasContent = (content != null);

                int numChildNodes = 0;
                int insertIdx = list.Count;

                if ((int)node->Type < 1000)
                {
                    // step inside
                    if (node->ChildNode != null)
                    {
                        hasParsableChildNodes = RecursiveAppendParsableChildNodes(node->ChildNode, depth + 1, numChildNodes, list, debugPath + "," + numChildNodes);
                        numChildNodes++;

                        AtkResNode* linkNode = node->ChildNode;

                        while (linkNode->PrevSiblingNode != null)
                        {
                            var hasParsableSibling = RecursiveAppendParsableChildNodes(linkNode->PrevSiblingNode, depth + 1, numChildNodes, list, debugPath + "," + numChildNodes);
                            hasParsableChildNodes = hasParsableChildNodes || hasParsableSibling;
                            linkNode = linkNode->PrevSiblingNode;
                            numChildNodes++;
                        }

                        // no need to check next siblings here?
                    }
                }
                else
                {
                    var compNode = (AtkComponentNode*)node;
                    for (int idx = 0; idx < compNode->Component->UldManager.NodeListCount; idx++)
                    {
                        hasParsableChildNodes = RecursiveAppendParsableChildNodes(compNode->Component->UldManager.NodeList[idx], depth + 1, numChildNodes, list, debugPath + "," + numChildNodes);
                        numChildNodes++;
                    }
                }

                if (hasParsableChildNodes || hasContent)
                {
                    list.Insert(insertIdx, new ParsableNode() { nodeAddr = (ulong)node, content = content, childIdx = childIdx, numChildren = numChildNodes, depth = depth, debugPath = debugPath, type = node->Type });
                }
            }

            return hasParsableChildNodes || hasContent;
        }

        public static unsafe void LogParsableNodes(AtkResNode* node)
        {
            var list = new List<ParsableNode>();
            RecursiveAppendParsableChildNodes(node, 0, 0, list, "");

            foreach (var entry in list)
            {
                var prefix = entry.depth > 0 ? new string(' ', entry.depth * 2) : "";
                Service.logger.Info($"{prefix}> '{entry.content}' idx:{entry.childIdx}, children:{entry.numChildren}, type:{entry.type}, addr:{entry.nodeAddr:X}, path:{entry.debugPath}");
            }
        }
#endif // DEBUG
    }
}
