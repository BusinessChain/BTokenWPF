using System;
using System.Linq;
using System.Collections.Generic;

namespace BTokenLib
{
  public partial class Network
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
          int indexHeaderAncesor = Locator.FindIndex(h => h.Hash.IsAllBytesEqual(header.HashPrevious));

          if (indexHeaderAncesor == -1)
            throw new ProtocolException($"Header {header} does not connect to locator.");

          if (Locator[indexHeaderAncesor].HeaderNext?.Hash.IsAllBytesEqual(header.Hash) == true)
            Locator[indexHeaderAncesor] = Locator[indexHeaderAncesor].HeaderNext;
          else
          {
            header.AppendToHeader(Locator[indexHeaderAncesor]);
            HeaderRoot = header;
            HeaderTip = header;
          }
        }
        else if (header.HashPrevious.IsAllBytesEqual(HeaderTip.Hash))
        {
          header.AppendToHeader(HeaderTip);
          HeaderTip.HeaderNext = header;
          HeaderTip = header;
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
}
