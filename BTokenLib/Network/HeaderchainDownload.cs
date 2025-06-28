using System;
using System.Linq;
using System.Collections.Generic;

namespace BTokenLib
{
  public class HeaderchainDownload
  {
    public List<Header> Locator;

    public Header HeaderTipTokenInitial;

    public Header HeaderTip;
    public Header HeaderRoot;

    public bool IsFork;


    public HeaderchainDownload(List<Header> locator)
    {
      HeaderTipTokenInitial = locator.First();
      Locator = locator;
    }

    public bool TryInsertHeaders(List<Header> headers)
    {
      foreach (Header header in headers)
      {
        if (Locator.Any(h => h.Hash.IsAllBytesEqual(header.Hash)))
          return false;

        if (HeaderRoot == null)
        {
          int indexHeaderAncestor = Locator.FindIndex(h => h.Hash.IsAllBytesEqual(header.HashPrevious));

          if (indexHeaderAncestor == -1)
            return false;

          IsFork = indexHeaderAncestor != 0;

          Header headerAncestor = Locator[indexHeaderAncestor];

          if (headerAncestor.HeaderNext?.Hash.IsAllBytesEqual(header.Hash) == true)
            headerAncestor = headerAncestor.HeaderNext;
          else
          {
            header.AppendToHeader(headerAncestor);
            HeaderRoot = header;
            HeaderTip = header;

            Locator = Locator.Skip(indexHeaderAncestor).ToList();
            Locator.Insert(0, header);
          }
        }
        else if (header.HashPrevious.IsAllBytesEqual(HeaderTip.Hash))
        {
          header.AppendToHeader(HeaderTip);
          HeaderTip.HeaderNext = header;
          HeaderTip = header;

          Locator[0] = header;
        }
        else return false;
      }

      return true;
    }

    public bool IsStrongerThan(Header header)
    {
      if (HeaderTip == null)
        return false;

      return HeaderTip.DifficultyAccumulated > header.DifficultyAccumulated;
    }

    public bool IsWeakerThan(Header header)
    {
      return HeaderTip == null || HeaderTip.DifficultyAccumulated < header.DifficultyAccumulated;
    }

    public int GetHeightAncestor()
    {
      return HeaderRoot.HeaderPrevious.Height;
    }

    public override string ToString()
    {
      return $"{Locator.First()} ... {Locator.Last()}";
    }
  }
}
