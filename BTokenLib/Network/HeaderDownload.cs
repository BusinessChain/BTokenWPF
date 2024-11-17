using System;
using System.Linq;
using System.Collections.Generic;

namespace BTokenLib
{
  public class HeaderDownload
  {
    public List<Header> Locator;

    public Header HeaderTip;
    public Header HeaderRoot; // The first header that is inserted into HeaderDownload
    public Header HeaderAncestor;
    public Header HeaderStopAcceptDuplicates;


    public HeaderDownload(List<Header> locator)
    {
      Locator = locator;
    }

    public virtual void InsertHeader(Header header)
    {
      if (HeaderAncestor == null)
      {
        HeaderAncestor = Locator.Find(h => h.Hash.IsAllBytesEqual(header.HashPrevious))
          ?? throw new ProtocolException($"Header {header} does not connect to locator.");

        int indexNext = Locator.IndexOf(HeaderAncestor) + 1;
        HeaderStopAcceptDuplicates = (Locator.Count > indexNext) ? Locator[indexNext] : null;

        HeaderRoot = header;
      }

      if (HeaderStopAcceptDuplicates?.Hash.IsAllBytesEqual(header.Hash) == true)
        throw new ProtocolException($"Headers are all duplicates.");

      if (HeaderAncestor.HeaderNext?.Hash.IsAllBytesEqual(header.Hash) == true)
      {
        HeaderAncestor = HeaderAncestor.HeaderNext;

        header.AppendToHeader(HeaderAncestor);
        HeaderRoot = header;
        HeaderTip = header;
      }
      else
      {
        header.AppendToHeader(HeaderTip);
        HeaderTip.HeaderNext = header;
        HeaderTip = header;
      }
    }

    public override string ToString()
    {
      return $"{Locator.First()} ... {Locator.Last()}";
    }
  }
}
