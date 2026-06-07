using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
namespace Saucy.TripleTriad.Utils;

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

            if (node->ChildNode != null)
            {
                RecursiveAppendChildNodes(node->ChildNode, listAddr);

                var linkNode = node->ChildNode;
                while (linkNode->PrevSiblingNode != null)
                {
                    RecursiveAppendChildNodes(linkNode->PrevSiblingNode, listAddr);
                    linkNode = linkNode->PrevSiblingNode;
                }
            }
        }
    }

    private static unsafe AtkResNode*[]? ConvertToNodeArr(List<ulong> listAddr)
    {
        if (listAddr.Count > 0)
        {
            var typedArr = new AtkResNode*[listAddr.Count];
            for (var idx = 0; idx < listAddr.Count; idx++)
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

    public static unsafe AtkResNode* GetChildNode(AtkResNode* node) => node != null ? node->ChildNode : null;

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
                        ? Marshal.PtrToStringAnsi((nint)texFileNameStdString->Buffer)
                        : Marshal.PtrToStringAnsi((nint)texFileNameStdString->BufferPtr);

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
            var text = Marshal.PtrToStringUTF8(new(textNode->NodeText.StringPtr));
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
        if (node == null)
        {
            return new(1, 1);
        }
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
}
