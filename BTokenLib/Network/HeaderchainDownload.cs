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

    public void InsertHeader(Header header)
    {
      if (Locator.Any(h => h.Hash.IsAllBytesEqual(header.Hash)))
        throw new ProtocolException($"Header is duplicate.");

      if (HeaderRoot == null)
      {
        int indexHeaderAncestor = Locator.FindIndex(h => h.Hash.IsAllBytesEqual(header.HashPrevious));

        if (indexHeaderAncestor == -1)
          throw new ProtocolException($"Header {header} does not connect to locator.");

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
      else
        throw new ProtocolException($"Header {header} does not connect to previous header {HeaderTip}.");
    }

    public override string ToString()
    {
      return $"{Locator.First()} ... {Locator.Last()}";
    }
  }
}
