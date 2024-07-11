library(dplyr)
library(ggplot2)

theme_set(theme_bw() + theme(axis.line = element_line(linewidth = 0.3), panel.border = element_blank()))

throughput = tibble(io = c(rep("buffered", 4), rep("unbuffered", 4)),
                    threads = rep(c(1, 2, 4, 8), 2), 
                    driveTransferRate = c(1.2, 2.2, 3.5, 4.6, 1.0, 2.0, 3.5, 5.8), # GB/s
                    ddrBandwidth = c(4.2 + 2.9, 6.0 +	4.8, 11.9 +	8.9, 18.9	+ 13.2, 2.3 + 2.0, 4.3 + 3.8, 7.9 + 6.9, 13.1 + 11.5), # GB/s
                    driveDemand = c(rep(driveTransferRate[1], 4), rep(driveTransferRate[5], 4)) * threads, # GB/s
                    ddrDemand = c(rep(ddrBandwidth[1], 4), rep(ddrBandwidth[5], 4)) * threads, # GB/s
                    practicalDdrLimit = 0.6 * 51.2) %>% # GB/s
  mutate(throughputModel = driveDemand * (1 - 0.7 * ddrDemand / (ddrDemand + practicalDdrLimit)))

ggplot() +
  geom_line(aes(x = threads, y = practicalDdrLimit, color = "DDR limit", linetype = "DDR limit"), throughput) +
  geom_point(aes(x = threads, y = driveTransferRate, color = "drive"), throughput) +
  geom_point(aes(x = threads, y = ddrBandwidth, color = "DDR"), throughput) +
  geom_line(aes(x = threads, y = driveDemand, color = "drive demand", group = io, linetype = "drive demand"), throughput) +
  geom_line(aes(x = threads, y = ddrDemand, color = "DDR demand", group = io, linetype = "DDR demand"), throughput) +
  geom_line(aes(x = threads, y = throughputModel, color = "drive", group = io, linetype = "drive"), throughput) +
  coord_cartesian(ylim = c(0, 40)) +
  labs(x = "read threads", y = bquote("GB s"^-1), color = NULL, linetype = NULL) +
  scale_linetype_manual(breaks = c("drive", "DDR demand", "drive demand", "DDR limit"), values = c("solid", "longdash", "longdash", "dashed")) +
  scale_x_continuous(breaks = c(1, 2, 4, 8, 16), minor_breaks = c(3, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15))
