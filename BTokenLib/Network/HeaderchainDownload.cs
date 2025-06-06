using System;
using System.Linq;
using System.Collections.Generic;

namespace BTokenLib
{
  public class HeaderchainDownload
  {
    public List<Header> Locator;

    public Header HeaderTip;
    public Header HeaderRoot;


    public HeaderchainDownload(List<Header> locator)
    {
      Locator = locator;
    }

    public bool TryInsertHeader(Header header, out bool flagIsHeaderRoot)
    {
      flagIsHeaderRoot = false;

      if (Locator.Any(h => h.Hash.IsAllBytesEqual(header.Hash)))
        return false;

      if (HeaderRoot == null)
      {
        int indexHeaderAncestor = Locator.FindIndex(h => h.Hash.IsAllBytesEqual(header.HashPrevious));

        if (indexHeaderAncestor == -1)
          return false;

        Header headerAncestor = Locator[indexHeaderAncestor];

        if (headerAncestor.HeaderNext?.Hash.IsAllBytesEqual(header.Hash) == true)
          headerAncestor = headerAncestor.HeaderNext;
        else
        {
          header.AppendToHeader(headerAncestor);
          HeaderRoot = header;
          flagIsHeaderRoot = false;
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
      else
        return false;

      return true;
    }

    public override string ToString()
    {
      return $"{Locator.First()} ... {Locator.Last()}";
    }
  }
}
